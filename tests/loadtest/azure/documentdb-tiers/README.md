# DocumentDB metal-tier infrastructure

Azure Cosmos DB for MongoDB vCore clusters across four tiers (Bronze/Silver/Gold/Platinum) — private-only, observable via Log Analytics, reachable from the in-cluster `alpine` pod in `hex-scaffold` namespace.

## Topology

| Metal | Cluster | Tier | Disk | HA | Subnet | CIDR |
|---|---|---|---|---|---|---|
| Bronze | `documentdb-bronze` | M10 | 32 GiB | off | `pe-sub-ddb-bronze` | 10.0.10.0/24 (pre-existed) |
| Silver | `documentdb-silver` | M20 | 32 GiB | on | `pe-sub-ddb-silver` | 10.0.11.0/24 |
| Gold | `documentdb-gold` | M30 | 32 GiB | on | `pe-sub-ddb-gold` | 10.0.12.0/24 |
| Platinum | `documentdb-platinum` | M40 | 32 GiB | on | `pe-sub-ddb-platinum` | 10.0.13.0/24 |

Server version `8.0`, single shard, public access **disabled**, one private endpoint per cluster bound to the existing `privatelink.mongocluster.cosmos.azure.com` zone (already linked to `aks-vnet`).

## Prereqs

- `az` CLI logged in to subscription `lcamargoreis-microsoft-subscription` (`df21ed78-…`)
- `cosmosdb-preview` extension installed (`az extension add -n cosmosdb-preview`)
- `kubectl` context pointed at the AKS cluster hosting `hex-scaffold/alpine`
- The `alpine` pod present in namespace `hex-scaffold`

## Run

Provision (~15 min wall, parallel cluster creates):
```bash
# Source the password from your secret store. NEVER commit the literal value.
# Example with az keyvault: ADMIN_PWD=$(az keyvault secret show --vault-name <vault> --name documentdb-admin --query value -o tsv)
export ADMIN_PWD='<set-from-secret-store>'
./provision.sh
```

> The literal `$` in any chosen password requires single-quoting on macOS zsh and bash to defeat shell expansion.

Verify TCP reachability of port 10260 from inside the cluster:
```bash
./verify.sh
```

Tear down everything (will prompt; pass `--yes` to skip):
```bash
./teardown.sh
```

## Outputs

- Per-tier provisioning logs: `.run-logs/<metal>.log`
- Per-tier exit codes: `.run-logs/<metal>.exit`
- Diagnostic settings sink to Log Analytics workspace `aks-test-workspace` in `aks-test-rg`
  - log category: `vCoreMongoRequests`
  - metric category: `AllMetrics`

## Connection string

Pull at runtime from each cluster (admin user `dbuser`):
```bash
az cosmosdb mongocluster show -g resources-test-rg -c documentdb-<metal> \
  --query connectionString -o tsv
```

The default database `db` is **not** auto-created — Mongo creates databases lazily on first insert. From the alpine pod:
```bash
kubectl exec -it pod/alpine -n hex-scaffold -- sh
# install mongosh in the pod (or use a sidecar image), then:
mongosh "<connection-string>" --eval 'db.getSiblingDB("db").bootstrap.insertOne({ok:1})'
```

## Cost

Approximate monthly run-rate (24×7, brazilsouth list prices):

| Tier | Compute | HA | ~$/mo |
|---|---|---|---|
| M10 | 0.5 vCPU, 2 GiB RAM | off | $50 |
| M20 | 1 vCPU, 4 GiB RAM | on | $180 |
| M30 | 2 vCPU, 8 GiB RAM | on | $400 |
| M40 | 4 vCPU, 16 GiB RAM | on | $800 |
| **Total** | | | **~$1,430/mo** |

Run `./teardown.sh` when finished.

## Notes

- **Platinum fallback:** if M40 isn't available in `brazilsouth`, the script falls back to `$PLATINUM_TIER_FALLBACK` (default M30) and continues. To override: `PLATINUM_TIER_FALLBACK=M50 ./provision.sh`.
- **Bronze subnet** (`pe-sub-ddb-bronze`, 10.0.10.0/24) pre-existed and is left in place by `teardown.sh` — only the silver/gold/platinum subnets are removed.
- An **orphaned PE** (`documentdb-bronze-c-pe1`) from a prior provisioning attempt is auto-deleted by `provision.sh` Step 0.
- The **private DNS zone** and its **vnet link** to `aks-vnet` already existed before this exercise and are not modified or torn down.
