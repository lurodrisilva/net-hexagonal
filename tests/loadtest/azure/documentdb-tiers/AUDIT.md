# Provisioning audit — Azure Cosmos DB for MongoDB vCore (Bronze/Silver/Gold/Platinum)

> Filled in by Phase 4 (architect / security-reviewer / code-reviewer) after `provision.sh` and `verify.sh` complete. All values populated from live `az` reads — no static placeholders.

## Provisioned resources

### Clusters

| Metal | Cluster | Tier (effective) | Disk | HA | publicNetworkAccess | provisioningState | FQDN |
|---|---|---|---|---|---|---|---|
| Bronze | `documentdb-bronze` | _M10_ | _32 GiB_ | _false_ | _Disabled_ | _Succeeded_ | _<filled>_ |
| Silver | `documentdb-silver` | _M20_ | _32 GiB_ | _true_ | _Disabled_ | _Succeeded_ | _<filled>_ |
| Gold | `documentdb-gold` | _M30_ | _32 GiB_ | _true_ | _Disabled_ | _Succeeded_ | _<filled>_ |
| Platinum | `documentdb-platinum` | _M40 or fallback_ | _32 GiB_ | _true_ | _Disabled_ | _Succeeded_ | _<filled>_ |

### Networking

| Metal | Subnet | CIDR | Private endpoint | PE NIC private IP |
|---|---|---|---|---|
| Bronze | `pe-sub-ddb-bronze` | 10.0.10.0/24 (pre-existing) | `documentdb-bronze-pe` | _<filled>_ |
| Silver | `pe-sub-ddb-silver` | 10.0.11.0/24 | `documentdb-silver-pe` | _<filled>_ |
| Gold | `pe-sub-ddb-gold` | 10.0.12.0/24 | `documentdb-gold-pe` | _<filled>_ |
| Platinum | `pe-sub-ddb-platinum` | 10.0.13.0/24 | `documentdb-platinum-pe` | _<filled>_ |

Private DNS zone: `privatelink.mongocluster.cosmos.azure.com` (already linked to aks-vnet, link `kq36zqmmz5psq`).

Orphan PE removed at Step 0: `documentdb-bronze-c-pe1` (was Disconnected, underlying cluster gone).

### Observability

Each cluster: one diagnostic setting `<cluster>-diag` → workspace `aks-test-workspace` (`/subscriptions/df21ed78-…/resourceGroups/aks-test-rg/providers/Microsoft.OperationalInsights/workspaces/aks-test-workspace`):
- log: `vCoreMongoRequests`
- metric: `AllMetrics`

## Connectivity verification (`verify.sh` output)

| Cluster | FQDN | TCP :10260 |
|---|---|---|
| `documentdb-bronze` | _<filled>_ | _open / FAIL_ |
| `documentdb-silver` | _<filled>_ | _open / FAIL_ |
| `documentdb-gold` | _<filled>_ | _open / FAIL_ |
| `documentdb-platinum` | _<filled>_ | _open / FAIL_ |

## Phase 4 reviewer notes

### architect
- [ ] All four tiers provisioned (M10/M20/M30/M40 or fallback)
- [ ] HA matrix correct (Bronze=off, others=on)
- [ ] Subnets allocated per `/24` plan, no CIDR collisions
- [ ] Private DNS zone reused, not duplicated
- [ ] Diagnostic categories include both `vCoreMongoRequests` AND `AllMetrics`

### security-reviewer
- [ ] `publicNetworkAccess == Disabled` on all four clusters
- [ ] Admin password not committed to repo (verify via `git ls-files` + grep)
- [ ] Provision/teardown scripts read password from env, not hard-coded
- [ ] PE policies: `privateEndpointNetworkPolicies=Disabled` on all four PE subnets
- [ ] No NSG/firewall rules unexpectedly attached to PE subnets

### code-reviewer
- [ ] `set -euo pipefail` on all three scripts
- [ ] Idempotency: re-running `provision.sh` is a no-op
- [ ] Quoting correct around `$ADMIN_PWD` in CLI invocations
- [ ] `az` errors propagate via background-job exit codes (`pids[]` waited individually)
- [ ] Teardown prompts for confirmation; supports `--yes` for non-interactive
