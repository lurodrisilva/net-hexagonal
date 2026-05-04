#!/usr/bin/env bash
#
# Run Steps 3-6 (disable public access → PE → DNS bind → diagnostic) for a
# single, already-Succeeded Mongo vCore cluster. Used to backfill the
# downstream wiring after a cluster create has finished out-of-band.
#
# Usage: finish-tier.sh <metal>     # metal in {bronze,silver,gold,platinum}

set -euo pipefail

METAL="${1:?metal name required: bronze|silver|gold|platinum}"

SUB="df21ed78-be77-40e3-9184-38eb23175791"
LOC="brazilsouth"
NET_RG="aks-test-rg"
CLUSTER_RG="resources-test-rg"
VNET="aks-vnet"
DNS_ZONE="privatelink.mongocluster.cosmos.azure.com"
WORKSPACE_ID="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.OperationalInsights/workspaces/aks-test-workspace"

CLUSTER="documentdb-${METAL}"
SUBNET="pe-sub-ddb-${METAL}"
CLUSTER_ID="/subscriptions/${SUB}/resourceGroups/${CLUSTER_RG}/providers/Microsoft.DocumentDB/mongoClusters/${CLUSTER}"
SUBNET_ID="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.Network/virtualNetworks/${VNET}/subnets/${SUBNET}"
DNS_ZONE_ID="/subscriptions/${SUB}/resourceGroups/${NET_RG}/providers/Microsoft.Network/privateDnsZones/${DNS_ZONE}"

echo "[$(date +%H:%M:%S)] [$METAL] Steps 3-6"

# Step 3: disable public network access (idempotent — issue PUT only if not already Disabled)
current_pna=$(az cosmosdb mongocluster show -g "$CLUSTER_RG" -c "$CLUSTER" \
                --query 'properties.publicNetworkAccess' -o tsv)
if [[ "$current_pna" == "Disabled" ]]; then
  echo "[$METAL]   public access already Disabled, skipping"
else
  echo "[$METAL]   disabling public network access"
  az resource update \
    --ids "$CLUSTER_ID" \
    --api-version 2024-07-01 \
    --set properties.publicNetworkAccess=Disabled \
    --query 'properties.publicNetworkAccess' -o tsv
fi

# Step 4: PE
if az network private-endpoint show -g "$CLUSTER_RG" -n "${CLUSTER}-pe" >/dev/null 2>&1; then
  echo "[$METAL]   PE exists, skipping"
else
  echo "[$METAL]   creating PE ${CLUSTER}-pe"
  az network private-endpoint create \
    -g "$CLUSTER_RG" -n "${CLUSTER}-pe" \
    --subnet "$SUBNET_ID" \
    --private-connection-resource-id "$CLUSTER_ID" \
    --group-id mongoCluster \
    --connection-name "${CLUSTER}-conn" \
    --location "$LOC" --query 'name' -o tsv
fi

# Step 5: DNS zone group
# NOTE: `az network private-endpoint dns-zone-group show` returns `{}` with
# exit 0 for missing resources (CLI bug). Check the response body for the
# `name` field instead of relying on exit code.
existing_dnsgrp=$(az network private-endpoint dns-zone-group show \
                    -g "$CLUSTER_RG" --endpoint-name "${CLUSTER}-pe" -n "${CLUSTER}-dnsgrp" \
                    --query 'name' -o tsv 2>/dev/null || true)
if [[ -n "$existing_dnsgrp" ]]; then
  echo "[$METAL]   DNS zone group exists, skipping"
else
  echo "[$METAL]   binding PE to private DNS zone"
  az network private-endpoint dns-zone-group create \
    -g "$CLUSTER_RG" --endpoint-name "${CLUSTER}-pe" \
    --name "${CLUSTER}-dnsgrp" \
    --private-dns-zone "$DNS_ZONE_ID" \
    --zone-name privatelink-mongocluster-cosmos-azure-com --query 'name' -o tsv
fi

# Step 6: diagnostic settings
if az monitor diagnostic-settings show \
     --resource "$CLUSTER_ID" --name "${CLUSTER}-diag" >/dev/null 2>&1; then
  echo "[$METAL]   diagnostic settings exist, skipping"
else
  echo "[$METAL]   creating diagnostic settings"
  az monitor diagnostic-settings create \
    --name "${CLUSTER}-diag" \
    --resource "$CLUSTER_ID" \
    --workspace "$WORKSPACE_ID" \
    --logs    '[{"category":"vCoreMongoRequests","enabled":true}]' \
    --metrics '[{"category":"AllMetrics","enabled":true}]' --query 'name' -o tsv
fi

echo "[$(date +%H:%M:%S)] [$METAL] done"
