#!/usr/bin/env bash
#
# Verify private-only TCP reachability of every Mongo vCore cluster from the
# in-cluster `alpine` pod (busybox `nc`). Resolves FQDN via cluster CoreDNS,
# which inherits the `privatelink.mongocluster.cosmos.azure.com` zone via the
# vnet link.

set -euo pipefail

CLUSTER_RG="resources-test-rg"
NS="hex-scaffold"
POD="alpine"
PORT=10260

CLUSTERS=(documentdb-bronze documentdb-silver documentdb-gold documentdb-platinum)

printf '%-22s %-65s %s\n' CLUSTER 'PE_FQDN (private)' TCP

# Mongo vCore uses `mongodb+srv://` connection strings — the cluster's host
# `<name>.mongocluster.cosmos.azure.com` only carries SRV records, not A. The
# actual A-record-resolvable hostname is published on the PE's DNS zone group
# (e.g. `fc-XXXX-000.privatelink.mongocluster.cosmos.azure.com`). We probe that.

failed=0
for cluster in "${CLUSTERS[@]}"; do
  pe_fqdn=$(az network private-endpoint dns-zone-group show \
              -g "$CLUSTER_RG" --endpoint-name "${cluster}-pe" -n "${cluster}-dnsgrp" \
              --query 'privateDnsZoneConfigs[0].recordSets[0].fqdn' -o tsv 2>/dev/null || true)

  if [[ -z "$pe_fqdn" ]]; then
    printf '%-22s %-65s %s\n' "$cluster" "(no PE DNS record)" "FAIL"
    failed=1
    continue
  fi

  if probe_err=$(kubectl exec "pod/${POD}" -n "$NS" -- nc -zv -w5 "$pe_fqdn" "$PORT" 2>&1 >/dev/null); then
    printf '%-22s %-65s %s\n' "$cluster" "$pe_fqdn" "open"
  else
    printf '%-22s %-65s %s  -- %s\n' "$cluster" "$pe_fqdn" "FAIL" "${probe_err//$'\n'/ | }"
    failed=1
  fi
done

exit $failed
