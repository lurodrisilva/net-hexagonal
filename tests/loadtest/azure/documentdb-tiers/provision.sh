#!/usr/bin/env bash
#
# Provision Azure Cosmos DB for MongoDB vCore clusters across the metal tiers
# (Bronze/Silver/Gold/Platinum) — private-only, observable via Log Analytics,
# reachable from the in-cluster `alpine` pod.
#
# Idempotent: every step checks for existence before creating. Safe to re-run.
#
# Required env:
#   ADMIN_PWD   admin password (literal, will be passed to az; single-quote
#               on macOS zsh to defeat shell expansion)
#
# Optional env:
#   PLATINUM_TIER_FALLBACK=M30   used if M40 is unsupported in the region
#
# Wall time: ~15 min (cluster creates run in parallel as background jobs).

set -euo pipefail

: "${ADMIN_PWD:?ADMIN_PWD env var is required (the cluster admin password)}"

SUB="df21ed78-be77-40e3-9184-38eb23175791"
LOC="brazilsouth"
NET_RG="aks-test-rg"
CLUSTER_RG="resources-test-rg"
VNET="aks-vnet"
DNS_ZONE="privatelink.mongocluster.cosmos.azure.com"
WORKSPACE_ID="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.OperationalInsights/workspaces/aks-test-workspace"
ADMIN_USER="dbuser"
SERVER_VERSION="8.0"
DISK_GB="32"
PLATINUM_TIER_FALLBACK="${PLATINUM_TIER_FALLBACK:-M30}"

LOGDIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/.run-logs"
mkdir -p "$LOGDIR"

log() { printf '[%s] %s\n' "$(date +%H:%M:%S)" "$*"; }

# Tier matrix: metal:cluster:tier:ha:subnet:cidr_octet
TIERS=(
  "bronze:documentdb-bronze:M10:false:pe-sub-ddb-bronze:10"
  "silver:documentdb-silver:M20:true:pe-sub-ddb-silver:11"
  "gold:documentdb-gold:M30:true:pe-sub-ddb-gold:12"
  "platinum:documentdb-platinum:M40:true:pe-sub-ddb-platinum:13"
)

# ---------- Prereq checks ----------
log "Verifying az login + subscription"
current_sub=$(az account show --query id -o tsv)
[[ "$current_sub" == "$SUB" ]] || { echo "Wrong subscription ($current_sub); expected $SUB"; exit 1; }

log "Verifying kubectl context can reach hex-scaffold/alpine"
kubectl get pod alpine -n hex-scaffold --no-headers >/dev/null

# ---------- Step 0: orphan PE cleanup ----------
log "Step 0: cleaning up orphan PE documentdb-bronze-c-pe1 (if present)"
if az network private-endpoint show -g "$CLUSTER_RG" -n documentdb-bronze-c-pe1 >/dev/null 2>&1; then
  az network private-endpoint delete -g "$CLUSTER_RG" -n documentdb-bronze-c-pe1
  log "  deleted orphan PE"
else
  log "  not present (skipping)"
fi

# ---------- Step 1: subnets (silver, gold, platinum) ----------
log "Step 1: subnets"
for entry in "${TIERS[@]}"; do
  IFS=':' read -r metal cluster tier ha subnet octet <<<"$entry"
  if az network vnet subnet show -g "$NET_RG" --vnet-name "$VNET" -n "$subnet" >/dev/null 2>&1; then
    log "  $subnet exists, skipping"
  else
    log "  creating $subnet (10.0.${octet}.0/24)"
    az network vnet subnet create \
      -g "$NET_RG" --vnet-name "$VNET" -n "$subnet" \
      --address-prefixes "10.0.${octet}.0/24" \
      --private-endpoint-network-policies Disabled \
      --private-link-service-network-policies Enabled >/dev/null
  fi
done

# ---------- Step 2-6: per-tier pipeline (parallel) ----------
# Each metal runs as a background job: create cluster → disable public → PE →
# DNS-zone-group → diagnostic. Logs to $LOGDIR/<metal>.log. Exit code captured
# in $LOGDIR/<metal>.exit.

provision_tier() {
  local metal="$1" cluster="$2" tier="$3" ha="$4" subnet="$5"
  local logfile="$LOGDIR/${metal}.log"
  exec >"$logfile" 2>&1
  set -euo pipefail

  echo "=== ${metal} (${cluster} / tier=${tier} / ha=${ha}) ==="

  # 2. Create cluster (with M40→M30 fallback for platinum if region unsupported)
  if az cosmosdb mongocluster show -g "$CLUSTER_RG" -c "$cluster" >/dev/null 2>&1; then
    echo "  cluster exists, skipping create"
  else
    local effective_tier="$tier"
    if ! az cosmosdb mongocluster create \
           -g "$CLUSTER_RG" -c "$cluster" \
           --location "$LOC" \
           --administrator-login "$ADMIN_USER" \
           --administrator-login-password "$ADMIN_PWD" \
           --server-version "$SERVER_VERSION" \
           --shard-node-tier "$effective_tier" \
           --shard-node-ha "$ha" \
           --shard-node-disk-size-gb "$DISK_GB" \
           --shard-node-count 1; then
      if [[ "$metal" == "platinum" ]]; then
        echo "  M40 create failed; retrying Platinum at fallback tier $PLATINUM_TIER_FALLBACK"
        effective_tier="$PLATINUM_TIER_FALLBACK"
        az cosmosdb mongocluster create \
          -g "$CLUSTER_RG" -c "$cluster" \
          --location "$LOC" \
          --administrator-login "$ADMIN_USER" \
          --administrator-login-password "$ADMIN_PWD" \
          --server-version "$SERVER_VERSION" \
          --shard-node-tier "$effective_tier" \
          --shard-node-ha "$ha" \
          --shard-node-disk-size-gb "$DISK_GB" \
          --shard-node-count 1
      else
        return 1
      fi
    fi
  fi

  local cluster_id="/subscriptions/${SUB}/resourceGroups/${CLUSTER_RG}/providers/Microsoft.DocumentDB/mongoClusters/${cluster}"

  # 3. Disable public network access
  echo "  disabling public network access"
  az resource update \
    --ids "$cluster_id" \
    --api-version 2024-07-01 \
    --set properties.publicNetworkAccess=Disabled \
    --query "properties.publicNetworkAccess" -o tsv

  # 4. Private endpoint
  # NOTE: pass the full subnet resource ID, NOT --vnet-name + bare subnet name.
  # When `--vnet-name` is given, az looks up the VNet in the same RG as `-g`,
  # which is $CLUSTER_RG. The VNet lives in $NET_RG, hence the explicit ID.
  local subnet_id="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.Network/virtualNetworks/${VNET}/subnets/${subnet}"
  if az network private-endpoint show -g "$CLUSTER_RG" -n "${cluster}-pe" >/dev/null 2>&1; then
    echo "  PE exists, skipping"
  else
    echo "  creating PE ${cluster}-pe in subnet ${subnet}"
    az network private-endpoint create \
      -g "$CLUSTER_RG" -n "${cluster}-pe" \
      --subnet "$subnet_id" \
      --private-connection-resource-id "$cluster_id" \
      --group-id mongoCluster \
      --connection-name "${cluster}-conn" \
      --location "$LOC" >/dev/null
  fi

  # 5. PE → private DNS zone group
  # NOTE: `az network private-endpoint dns-zone-group show` returns `{}` with
  # exit 0 for missing resources (CLI bug). Check `name` in response instead.
  local existing_dnsgrp
  existing_dnsgrp=$(az network private-endpoint dns-zone-group show \
                      -g "$CLUSTER_RG" --endpoint-name "${cluster}-pe" -n "${cluster}-dnsgrp" \
                      --query 'name' -o tsv 2>/dev/null || true)
  if [[ -n "$existing_dnsgrp" ]]; then
    echo "  DNS zone group exists, skipping"
  else
    echo "  binding PE to private DNS zone"
    local dns_zone_id="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.Network/privateDnsZones/${DNS_ZONE}"
    az network private-endpoint dns-zone-group create \
      -g "$CLUSTER_RG" --endpoint-name "${cluster}-pe" \
      --name "${cluster}-dnsgrp" \
      --private-dns-zone "$dns_zone_id" \
      --zone-name privatelink-mongocluster-cosmos-azure-com >/dev/null
  fi

  # 6. Diagnostic settings
  if az monitor diagnostic-settings show \
       --resource "$cluster_id" --name "${cluster}-diag" >/dev/null 2>&1; then
    echo "  diagnostic settings exist, skipping"
  else
    echo "  creating diagnostic settings"
    az monitor diagnostic-settings create \
      --name "${cluster}-diag" \
      --resource "$cluster_id" \
      --workspace "$WORKSPACE_ID" \
      --logs    '[{"category":"vCoreMongoRequests","enabled":true}]' \
      --metrics '[{"category":"AllMetrics","enabled":true}]' >/dev/null
  fi

  echo "=== ${metal} done ==="
}
export -f provision_tier
export SUB LOC NET_RG CLUSTER_RG VNET DNS_ZONE WORKSPACE_ID ADMIN_USER ADMIN_PWD SERVER_VERSION DISK_GB PLATINUM_TIER_FALLBACK LOGDIR

log "Step 2-6: provisioning all four tiers in parallel; tail $LOGDIR/<metal>.log to follow"
pids=()
for entry in "${TIERS[@]}"; do
  IFS=':' read -r metal cluster tier ha subnet _ <<<"$entry"
  ( provision_tier "$metal" "$cluster" "$tier" "$ha" "$subnet"; rc=$?
    echo "$rc" > "$LOGDIR/${metal}.exit"
    exit "$rc" ) &
  pids+=($!)
  log "  spawned ${metal} (pid $!)"
done

log "Waiting for all four tier pipelines (this is the long one — ~15 min)"
status=0
for pid in "${pids[@]}"; do
  if ! wait "$pid"; then
    status=1
  fi
done

log "Per-tier exit codes:"
for entry in "${TIERS[@]}"; do
  IFS=':' read -r metal _ _ _ _ _ <<<"$entry"
  printf '  %-9s exit=%s\n' "$metal" "$(cat "$LOGDIR/${metal}.exit" 2>/dev/null || echo '?')"
done

if [[ $status -ne 0 ]]; then
  log "One or more tiers failed; inspect $LOGDIR/*.log"
  exit 1
fi

log "All four tiers provisioned successfully."
log "Run ./verify.sh to TCP-probe each cluster from the alpine pod."
