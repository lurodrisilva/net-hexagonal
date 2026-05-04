#!/usr/bin/env bash
#
# Retry Silver (M20) with HA disabled — Azure's API does not allow HA on M20
# (the lowest HA-capable tier is M30). This was discovered during the first
# provision pass: Silver create returned `bad_request: High Availability not
# available for 'M20' cluster tier`.
#
# Documented deviation from the original spec (which requested HA on for all
# non-Bronze tiers): M20 is platform-constrained.

set -euo pipefail

: "${ADMIN_PWD:?ADMIN_PWD env var is required}"

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

CLUSTER="documentdb-silver"
TIER="M20"
HA="false"   # platform constraint
SUBNET="pe-sub-ddb-silver"

LOGDIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/.run-logs"
mkdir -p "$LOGDIR"
LOG="$LOGDIR/silver-retry.log"

log() { printf '[%s] %s\n' "$(date +%H:%M:%S)" "$*" | tee -a "$LOG"; }

log "Silver retry — M20 with HA disabled (platform constraint)"

# Step 2: cluster
if az cosmosdb mongocluster show -g "$CLUSTER_RG" -c "$CLUSTER" >/dev/null 2>&1; then
  log "  cluster exists, skipping create"
else
  log "  creating $CLUSTER (M20, HA=false)"
  az cosmosdb mongocluster create \
    -g "$CLUSTER_RG" -c "$CLUSTER" \
    --location "$LOC" \
    --administrator-login "$ADMIN_USER" \
    --administrator-login-password "$ADMIN_PWD" \
    --server-version "$SERVER_VERSION" \
    --shard-node-tier "$TIER" \
    --shard-node-ha "$HA" \
    --shard-node-disk-size-gb "$DISK_GB" \
    --shard-node-count 1 2>&1 | tee -a "$LOG"
fi

CLUSTER_ID="/subscriptions/${SUB}/resourceGroups/${CLUSTER_RG}/providers/Microsoft.DocumentDB/mongoClusters/${CLUSTER}"

# Step 3: disable public access
log "  disabling public network access"
az resource update \
  --ids "$CLUSTER_ID" \
  --api-version 2024-07-01 \
  --set properties.publicNetworkAccess=Disabled \
  --query 'properties.publicNetworkAccess' -o tsv 2>&1 | tee -a "$LOG"

# Step 4: PE — pass full subnet resource ID (VNet lives in $NET_RG, not $CLUSTER_RG)
SUBNET_ID="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.Network/virtualNetworks/${VNET}/subnets/${SUBNET}"
if az network private-endpoint show -g "$CLUSTER_RG" -n "${CLUSTER}-pe" >/dev/null 2>&1; then
  log "  PE exists, skipping"
else
  log "  creating PE ${CLUSTER}-pe"
  az network private-endpoint create \
    -g "$CLUSTER_RG" -n "${CLUSTER}-pe" \
    --subnet "$SUBNET_ID" \
    --private-connection-resource-id "$CLUSTER_ID" \
    --group-id mongoCluster \
    --connection-name "${CLUSTER}-conn" \
    --location "$LOC" >/dev/null 2>&1 | tee -a "$LOG"
fi

# Step 5: DNS zone group
# NOTE: `az network private-endpoint dns-zone-group show` returns `{}` with
# exit 0 for missing resources (CLI bug). Check the `name` field in the
# response instead of relying on exit code.
EXISTING_DNSGRP=$(az network private-endpoint dns-zone-group show \
                    -g "$CLUSTER_RG" --endpoint-name "${CLUSTER}-pe" -n "${CLUSTER}-dnsgrp" \
                    --query 'name' -o tsv 2>/dev/null || true)
if [[ -n "$EXISTING_DNSGRP" ]]; then
  log "  DNS zone group exists, skipping"
else
  log "  binding PE to private DNS zone"
  DNS_ZONE_ID="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.Network/privateDnsZones/${DNS_ZONE}"
  az network private-endpoint dns-zone-group create \
    -g "$CLUSTER_RG" --endpoint-name "${CLUSTER}-pe" \
    --name "${CLUSTER}-dnsgrp" \
    --private-dns-zone "$DNS_ZONE_ID" \
    --zone-name privatelink-mongocluster-cosmos-azure-com >/dev/null 2>&1 | tee -a "$LOG"
fi

# Step 6: diagnostic settings
if az monitor diagnostic-settings show \
     --resource "$CLUSTER_ID" --name "${CLUSTER}-diag" >/dev/null 2>&1; then
  log "  diagnostic settings exist, skipping"
else
  log "  creating diagnostic settings"
  az monitor diagnostic-settings create \
    --name "${CLUSTER}-diag" \
    --resource "$CLUSTER_ID" \
    --workspace "$WORKSPACE_ID" \
    --logs    '[{"category":"vCoreMongoRequests","enabled":true}]' \
    --metrics '[{"category":"AllMetrics","enabled":true}]' >/dev/null 2>&1 | tee -a "$LOG"
fi

log "Silver retry complete."
