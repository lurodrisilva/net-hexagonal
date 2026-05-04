#!/usr/bin/env bash
#
# Tear down every Mongo vCore cluster, its private endpoint, and the three
# silver/gold/platinum subnets. Bronze subnet `pe-sub-ddb-bronze` is preserved
# (it pre-existed in the VNet before this exercise).
#
# Idempotent: missing resources are skipped silently.

set -euo pipefail

CLUSTER_RG="resources-test-rg"
NET_RG="aks-test-rg"
VNET="aks-vnet"

CLUSTERS=(documentdb-bronze documentdb-silver documentdb-gold documentdb-platinum)
SUBNETS_TO_DELETE=(pe-sub-ddb-silver pe-sub-ddb-gold pe-sub-ddb-platinum)

if [[ "${1:-}" != "--yes" ]]; then
  echo "This will permanently delete:"
  for c in "${CLUSTERS[@]}"; do echo "  - cluster $c (and its private endpoint)"; done
  for s in "${SUBNETS_TO_DELETE[@]}"; do echo "  - subnet $s"; done
  echo
  read -rp "Type 'delete' to proceed: " ans
  [[ "$ans" == "delete" ]] || { echo "Aborted."; exit 1; }
fi

log() { printf '[%s] %s\n' "$(date +%H:%M:%S)" "$*"; }

log "Step 1: deleting private endpoints + DNS zone groups"
for cluster in "${CLUSTERS[@]}"; do
  pe="${cluster}-pe"
  if az network private-endpoint show -g "$CLUSTER_RG" -n "$pe" >/dev/null 2>&1; then
    log "  deleting PE $pe"
    az network private-endpoint delete -g "$CLUSTER_RG" -n "$pe"
  fi
done

log "Step 2: deleting clusters (parallel)"
pids=()
for cluster in "${CLUSTERS[@]}"; do
  if az cosmosdb mongocluster show -g "$CLUSTER_RG" -c "$cluster" >/dev/null 2>&1; then
    log "  spawning delete for $cluster"
    az cosmosdb mongocluster delete -g "$CLUSTER_RG" -c "$cluster" --yes &
    pids+=($!)
  fi
done
for pid in "${pids[@]}"; do wait "$pid" || true; done

log "Step 3: deleting subnets (silver/gold/platinum)"
for subnet in "${SUBNETS_TO_DELETE[@]}"; do
  if az network vnet subnet show -g "$NET_RG" --vnet-name "$VNET" -n "$subnet" >/dev/null 2>&1; then
    log "  deleting subnet $subnet"
    az network vnet subnet delete -g "$NET_RG" --vnet-name "$VNET" -n "$subnet"
  fi
done

log "Teardown complete."
