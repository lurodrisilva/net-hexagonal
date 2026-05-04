#!/usr/bin/env bash
#
# Collect live state from the four provisioned Mongo vCore clusters and emit a
# filled-in audit table to stdout (markdown). Pipe into AUDIT.md or paste.
#
# Pure read-only.

set -euo pipefail

CLUSTER_RG="resources-test-rg"
NET_RG="aks-test-rg"
VNET="aks-vnet"
WORKSPACE_PATH="aks-test-rg/providers/Microsoft.OperationalInsights/workspaces/aks-test-workspace"

CLUSTERS=(
  "bronze:documentdb-bronze:pe-sub-ddb-bronze"
  "silver:documentdb-silver:pe-sub-ddb-silver"
  "gold:documentdb-gold:pe-sub-ddb-gold"
  "platinum:documentdb-platinum:pe-sub-ddb-platinum"
)

printf '## Live audit (collected %s)\n\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')"
cat <<'EOT'
### Clusters

| Metal | Cluster | Tier | Disk | HA | publicNetworkAccess | provisioningState | FQDN |
|---|---|---|---|---|---|---|---|
EOT

for entry in "${CLUSTERS[@]}"; do
  IFS=':' read -r metal cluster subnet <<<"$entry"
  data=$(az cosmosdb mongocluster show -g "$CLUSTER_RG" -c "$cluster" \
         --query '{tier:properties.nodeGroupSpecs[0].sku, disk:properties.nodeGroupSpecs[0].diskSizeGb, ha:properties.nodeGroupSpecs[0].enableHa, pna:properties.publicNetworkAccess, ps:properties.provisioningState, conn:connectionString}' \
         -o json 2>/dev/null) || data='{}'
  tier=$(echo "$data"  | jq -r '.tier  // "n/a"')
  disk=$(echo "$data"  | jq -r '.disk  // "n/a"')
  ha=$(echo "$data"    | jq -r 'if has("ha") and .ha != null then (.ha|tostring) else "n/a" end')
  pna=$(echo "$data"   | jq -r '.pna   // "n/a"')
  ps=$(echo "$data"    | jq -r '.ps    // "n/a"')
  fqdn=$(echo "$data"  | jq -r '.conn  // ""' | sed 's|.*@||;s|/?.*||;s|:.*||')
  printf "| %s | \`%s\` | %s | %s | %s | %s | %s | \`%s\` |\n" \
    "$metal" "$cluster" "$tier" "$disk" "$ha" "$pna" "$ps" "${fqdn:-n/a}"
done

cat <<'EOT'

### Networking

| Metal | Subnet | CIDR | Private endpoint | PE NIC private IP | DNS A record |
|---|---|---|---|---|---|
EOT

for entry in "${CLUSTERS[@]}"; do
  IFS=':' read -r metal cluster subnet <<<"$entry"
  cidr=$(az network vnet subnet show -g "$NET_RG" --vnet-name "$VNET" -n "$subnet" \
         --query addressPrefix -o tsv 2>/dev/null || echo "n/a")
  pe="${cluster}-pe"
  pe_exists=$(az network private-endpoint show -g "$CLUSTER_RG" -n "$pe" --query name -o tsv 2>/dev/null || echo "")
  if [[ -n "$pe_exists" ]]; then
    nic_id=$(az network private-endpoint show -g "$CLUSTER_RG" -n "$pe" \
             --query 'networkInterfaces[0].id' -o tsv 2>/dev/null)
    pe_ip=$(az network nic show --ids "$nic_id" \
            --query 'ipConfigurations[0].privateIPAddress' -o tsv 2>/dev/null || echo "n/a")
  else
    pe_ip="n/a"
  fi
  a_record=$(az network private-dns record-set a list \
             -g "$NET_RG" -z privatelink.mongocluster.cosmos.azure.com \
             --query "[?contains(name, '$cluster')].{n:name, ips:aRecords[].ipv4Address}" -o tsv 2>/dev/null \
             | head -1 || echo "n/a")
  printf "| %s | \`%s\` | %s | \`%s\` | \`%s\` | %s |\n" \
    "$metal" "$subnet" "$cidr" "$pe" "$pe_ip" "${a_record:-n/a}"
done

cat <<'EOT'

### Diagnostic settings

| Cluster | Setting | Workspace | Logs | Metrics |
|---|---|---|---|---|
EOT

for entry in "${CLUSTERS[@]}"; do
  IFS=':' read -r metal cluster _ <<<"$entry"
  cluster_id="$(az cosmosdb mongocluster show -g "$CLUSTER_RG" -c "$cluster" --query id -o tsv 2>/dev/null)"
  if [[ -z "$cluster_id" ]]; then
    printf "| \`%s\` | n/a | n/a | n/a | n/a |\n" "$cluster"
    continue
  fi
  diag=$(az monitor diagnostic-settings list --resource "$cluster_id" -o json 2>/dev/null || echo '[]')
  # `az monitor diagnostic-settings list` returns a top-level array; older shapes wrapped in {value:[]}.
  setting=$(echo "$diag" | jq -r 'if type=="array" then .[0].name else .value[0].name end // "n/a"')
  ws=$(echo "$diag"      | jq -r 'if type=="array" then .[0].workspaceId else .value[0].workspaceId end // "n/a"' | sed 's|.*workspaces/||')
  logs=$(echo "$diag"    | jq -r 'if type=="array" then [.[0].logs[]?    | select(.enabled) | .category] else [.value[0].logs[]?    | select(.enabled) | .category] end | join(",")')
  mets=$(echo "$diag"    | jq -r 'if type=="array" then [.[0].metrics[]? | select(.enabled) | .category] else [.value[0].metrics[]? | select(.enabled) | .category] end | join(",")')
  printf "| \`%s\` | %s | %s | %s | %s |\n" "$cluster" "$setting" "${ws:-n/a}" "${logs:-n/a}" "${mets:-n/a}"
done
