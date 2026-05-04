# Azure Cosmos DB for MongoDB vCore — Bronze/Silver/Gold/Platinum provisioning

**Date:** 2026-05-04
**Subscription:** `lcamargoreis-microsoft-subscription` (`df21ed78-be77-40e3-9184-38eb23175791`)
**Region:** `brazilsouth`
**Cluster RG:** `resources-test-rg`
**Network/Workspace RG:** `aks-test-rg`

> Filled in by `tests/loadtest/azure/documentdb-tiers/collect-audit.sh` after `provision.sh` and `verify.sh` complete. Sections marked `<TBD>` are populated post-run.

## Goal

Stand up four Azure Cosmos DB for MongoDB vCore clusters across the metal tiers (Bronze/Silver/Gold/Platinum), private-only, fully observable through Log Analytics, reachable from the in-cluster `alpine` pod in namespace `hex-scaffold`. Mirrors the metal-tier pattern previously used for PostgreSQL Flexible Server tests.

## Tier matrix (as deployed)

| Metal | Cluster | Tier | Disk | HA | provisioningState | publicNetworkAccess | Public host (mongodb+srv) |
|---|---|---|---|---|---|---|---|
| Bronze | `documentdb-bronze` | M10 | 32 GiB | false | Succeeded | Disabled | `documentdb-bronze.mongocluster.cosmos.azure.com` |
| Silver | `documentdb-silver` | **M20** ⚠ | 32 GiB | **false** ⚠ | Succeeded | Disabled | `documentdb-silver.mongocluster.cosmos.azure.com` |
| Gold | `documentdb-gold` | M30 | 32 GiB | true | Succeeded | Disabled | `documentdb-gold.mongocluster.cosmos.azure.com` |
| Platinum | `documentdb-platinum` | M40 | 32 GiB | true | Succeeded | Disabled | `documentdb-platinum.mongocluster.cosmos.azure.com` |

⚠ **Silver deviation from original spec.** The user requested HA on for every non-Bronze tier. Azure rejected the M20+HA combination with `bad_request: High Availability not available for 'M20' cluster tier`. Mongo vCore's HA support starts at M30. Silver was retried with `--shard-node-ha false` and is otherwise unchanged from the spec.

## Networking

| Metal | Subnet | CIDR | Private endpoint | PE NIC private IP | Status |
|---|---|---|---|---|---|
| Bronze | `pe-sub-ddb-bronze` | 10.0.10.0/24 (pre-existing) | `documentdb-bronze-pe` | 10.0.10.4 | Succeeded |
| Silver | `pe-sub-ddb-silver` | 10.0.11.0/24 | `documentdb-silver-pe` | 10.0.11.4 | Succeeded |
| Gold | `pe-sub-ddb-gold` | 10.0.12.0/24 | `documentdb-gold-pe` | 10.0.12.4 | Succeeded |
| Platinum | `pe-sub-ddb-platinum` | 10.0.13.0/24 | `documentdb-platinum-pe` | 10.0.13.4 | Succeeded |

- VNet: `aks-vnet` (10.0.0.0/16)
- Private DNS zone: `privatelink.mongocluster.cosmos.azure.com` (pre-existing in `aks-test-rg`, already linked to `aks-vnet` via link `kq36zqmmz5psq`)
- Cleanup performed: orphaned `documentdb-bronze-c-pe1` PE (status Disconnected, underlying cluster gone) was deleted before bronze provision

## Observability

Each cluster: one diagnostic setting `<cluster>-diag` → workspace `aks-test-workspace` (`/subscriptions/df21ed78-…/resourceGroups/aks-test-rg/providers/Microsoft.OperationalInsights/workspaces/aks-test-workspace`):

- log category `vCoreMongoRequests` (request-level traces, includes query payloads)
- metric category `AllMetrics`

| Cluster | Diagnostic setting | Logs enabled | Metrics enabled |
|---|---|---|---|
| `documentdb-bronze` | `documentdb-bronze-diag` | `vCoreMongoRequests` | `Saturation, Traffic, Latency` |
| `documentdb-silver` | `documentdb-silver-diag` | `vCoreMongoRequests` | `Saturation, Traffic, Latency` |
| `documentdb-gold` | `documentdb-gold-diag` | `vCoreMongoRequests` | `Saturation, Traffic, Latency` |
| `documentdb-platinum` | `documentdb-platinum-diag` | `vCoreMongoRequests` | `Saturation, Traffic, Latency` |

> Note: when the create call requests `AllMetrics`, Azure expands it into the canonical sub-categories `Saturation`, `Traffic`, `Latency` — equivalent semantically.

## Connectivity verification

Per-cluster TCP probe from `pod/alpine` in `hex-scaffold` namespace, port `10260` (Mongo vCore listener), via `nc -zv -w5 <fqdn> 10260`. DNS resolution flows: cluster CoreDNS → Azure Private DNS Zone (vnet-link inherits the privatelink record set).

| Cluster | PE FQDN (private) | TCP :10260 |
|---|---|---|
| `documentdb-bronze` | `fc-f5a30beeddd7-000.privatelink.mongocluster.cosmos.azure.com` | **open** ✅ |
| `documentdb-silver` | `fc-d177a7a4c977-000.privatelink.mongocluster.cosmos.azure.com` | **open** ✅ |
| `documentdb-gold` | `fc-fcea52c549bc-000.global.privatelink.mongocluster.cosmos.azure.com` | **open** ✅ |
| `documentdb-platinum` | `fc-d2503bc5bb7f-000.ro.global.privatelink.mongocluster.cosmos.azure.com` | **open** ✅ |

> Why are the FQDNs different shapes? Mongo vCore uses a `mongodb+srv://` connection string format. The cluster's public host name (e.g. `documentdb-gold.mongocluster.cosmos.azure.com`) carries SRV records, **not** A records — `nc` cannot probe it directly. The actual A-record-resolvable hostname is published on the PE's private DNS zone group:
> - **Bronze, Silver** (HA off): single record `fc-XXXX-000.privatelink…`
> - **Gold, Platinum** (HA on): two records, `.global.` (primary) + `.ro.global.` (read-only replica). `verify.sh` probes the first record returned, which differs by tier.

## Phase 4 reviewer findings (multi-perspective audit)

Three reviewers (architect, security-reviewer, code-reviewer) ran in parallel against the provisioning scripts. Findings and resolutions:

### CRITICAL
- **`collect-audit.sh:22-29`** — quoted heredoc swallowed `$(date …)` substitution → audit timestamp printed literally. **Fixed**: split into a `printf` for the dynamic line, kept the quoted heredoc for the static table header.

### HIGH
- **`provision.sh` parent `status` always 0** — the `( … echo $? > file ) &` subshell's last command was `echo $? > file`, which always succeeds; `wait $pid` therefore reported success even when a tier failed. **Fixed**: subshell now captures the function's `$?` into `rc` and explicitly `exit "$rc"` so the parent's `wait` reflects true status.
- **`verify.sh` swallowed `nc` stderr** — `>/dev/null 2>&1` hid all diagnostics on FAIL. **Fixed**: capture stderr into `$probe_err` and emit it inline in the FAIL row.
- **`README.md` showed literal admin password** as the example value for `ADMIN_PWD`. **Fixed**: replaced with `<set-from-secret-store>` placeholder and an `az keyvault secret show` example.

### MEDIUM
- Connection string (containing password) briefly held in subshell `$data` during audit — defense-in-depth concern (`/proc/<pid>/environ` exposure). _Acknowledged, not fixed_: the audit script runs only on operator command, and the password lives in the env vars regardless. Proper fix is not exporting `ADMIN_PWD`, which is incompatible with backgrounded subshells in the current parallel design.
- `provision.sh` non-idempotent public-access patch (issues a PUT every re-run). _Cosmetic; convergent_.
- `teardown.sh` `wait || true` masks deletion failures. _Tracked for follow-up_.
- M40-fallback path triggers on any create error (quota, name conflict), not just region-unsupported. _Tracked for follow-up_.

### LOW
- Several minor items (positional-arg fragility, jq null-vs-missing edge cases). _Tracked, not blocking_.

## Cost projection

Approximate brazilsouth list-price monthly run-rate (24×7):

| Tier | vCPU / RAM | HA | ~$/month |
|---|---|---|---|
| M10 | 0.5 / 2 GiB | off | $50 |
| M20 | 1 / 4 GiB | off (platform constraint) | $90 |
| M30 | 2 / 8 GiB | on | $400 |
| M40 | 4 / 16 GiB | on | $800 |
| **Total** | | | **~$1,340/mo** |

Run `tests/loadtest/azure/documentdb-tiers/teardown.sh` when the test rig is no longer needed.

## Operations runbook

Provision (idempotent, ~15 min wall):
```bash
export ADMIN_PWD='<from-secret-store>'
tests/loadtest/azure/documentdb-tiers/provision.sh
```

If Silver create fails (HA-related), retry without HA:
```bash
export ADMIN_PWD='<from-secret-store>'
tests/loadtest/azure/documentdb-tiers/retry-silver.sh
```

Verify reachability:
```bash
tests/loadtest/azure/documentdb-tiers/verify.sh
```

Collect a fresh audit (markdown to stdout):
```bash
tests/loadtest/azure/documentdb-tiers/collect-audit.sh
```

Teardown (prompts; pass `--yes` for non-interactive):
```bash
tests/loadtest/azure/documentdb-tiers/teardown.sh
```

## Files

- `tests/loadtest/azure/documentdb-tiers/provision.sh` — main idempotent provisioner (Steps 0-6)
- `tests/loadtest/azure/documentdb-tiers/retry-silver.sh` — Silver-only re-run with HA disabled
- `tests/loadtest/azure/documentdb-tiers/verify.sh` — TCP probe from alpine pod
- `tests/loadtest/azure/documentdb-tiers/collect-audit.sh` — read-only audit collector
- `tests/loadtest/azure/documentdb-tiers/teardown.sh` — destructive cleanup (prompts)
- `tests/loadtest/azure/documentdb-tiers/README.md` — runbook
- `tests/loadtest/azure/documentdb-tiers/AUDIT.md` — Phase 4 audit checklist + reviewer findings
