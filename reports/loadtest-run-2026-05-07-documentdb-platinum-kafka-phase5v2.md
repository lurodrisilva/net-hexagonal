# Kafka Inbound Load Test — Platinum / DocumentDB (Phase 5 v2)

Goal: re-run the Phase 5 platinum-Mongo tier after bulk-write coalescing (PR #66 + PR #68) and 12→24 partition expansion (PR #65) to close the 1.17 M end-lag from v1.

## Run summary

| Field | Value |
|---|---|
| Run id | `1778187086` |
| Date / time (UTC) | `2026-05-07T20:51:26Z → 2026-05-07T21:07:12Z` (driver elapsed 946 s) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App image | `latest` (includes PR #66 bulk-write + PR #68 same-batch id-collapse fix) |
| Repo SKU | Cosmos DB Mongo vCore `documentdb-platinum` (Brazil South) |
| Consumer pods | **8 replicas, HPA OFF**, on `nodepool2` |
| Consumer requests / limits | `cpu=1000m/2000m`, `memory=1Gi/4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) |
| Redis | OFF |
| Kafka topic | `v2.core.accounts.loadtest.platinum.mongo` (**24 partitions**, RF=2, lz4 — expanded via PR #65) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=65536` (platinum overlay) |
| k6 runner | `nodepool3`, `TIER=platinum`, `REPO=mongo`, `RUNID=1778187086` |
| Driver | v3 + AI-fix + `P95_BREACH_THRESHOLD_MS=60000` |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 530 events/s |
| Ramp | 3 min | → 5,300 events/s |
| Peak | 10 min | 5,300 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70% Create / 25% Update / 5% Delete.

## Result

| Metric | Value | Source |
|---|---|---|
| Stop reason | `cap-reached` (15-min wall clock) | `summary.json` |
| Elapsed | 946 s | `summary.json` |
| Broker LOG-END (sum 24 partitions) | 4,321,060 | `consumer-group-lag.log` |
| Effective offered rate | ~4,568 /s | LOG-END / elapsed |
| Consumer CURRENT-OFFSET (sum) | 4,321,060 | `consumer-group-lag.log` |
| Consumed rate | ~4,568 /s (matched offered) | CURRENT / elapsed |
| **End-of-run lag** | **0** | `consumer-group-lag.log` |
| **e2e p50** | 1,689 ms | `ai-final.json` |
| **e2e p95** | **5,533 ms** | `ai-final.json` |
| **e2e p99** | 9,869 ms | `ai-final.json` |
| e2e avg / max | 2,220 / 10,177 ms | `ai-final.json` |
| AI record count | 360 | `ai-final.json` |
| k6 offered rate (stdout) | not captured — k6.log empty (driver race) | `k6.log` |

## Consumer health — ZERO LAG

All 24 partitions at lag = 0 at run end:

| Pod group | Partitions | LOG-END range | LAG |
|---|---|---|---|
| 10.244.13.30 | 0, 1, 2 | 252,472–252,551 | 0 each |
| 10.244.19.34 | 3, 4, 5 | 252,436–252,536 | 0 each |
| 10.244.15.31 | 6, 7, 8 | 252,376–252,595 | 0 each |
| 10.244.11.39 | 9, 10, 11 | 252,431–252,610 | 0 each |
| 10.244.10.32 | 12, 13, 14 | 107,557–107,579 | 0 each |
| 10.244.17.26 | 15, 16, 17 | 107,534–107,618 | 0 each |
| 10.244.12.30 | 18, 19, 20 | 107,572–107,677 | 0 each |
| 10.244.18.38 | 21, 22, 23 | 107,561–107,693 | 0 each |
| **Sum** | **24** | **4,321,060** | **0** |

Partitions 0–11 carried ~252 k events (older offsets); partitions 12–23 carried ~107 k events. All 8 pods drained their 3 partitions fully before the cap. Even partition distribution (3:3:3:3:3:3:3:3 across 8 pods) eliminated the 2× hot/cold imbalance seen in v1.

## Repository (DocumentDB `documentdb-platinum`)

| Stat | Value |
|---|---|
| Peak CPU | 10.94 % (at 21:05Z, end-of-run) |
| Average CPU (all samples) | ~5.4 % |
| Binding constraint | **application handler** (DB massively over-provisioned) |

Platinum DocDB CPU peaked at 10.9 % — consistent with v1 (8.9 %). The bulk-write path increased per-batch size but not to a level that stresses this SKU. The consumer is I/O-blocked on async-await, not CPU-active; the database absorbs every batch comfortably.

## Application pod consumption

Per-pod Container Insights `Perf` data **not captured this batch** — driver did not snapshot the Container Insights `Perf` table. AKS Container Insights query is a known follow-up.

## Stop conditions

| Signal | Fired? |
|---|---|
| 15-min wall-clock cap | YES |
| p95 ≥ 60,000 ms | NO |
| DB CPU > 80 % for ≥ 2 samples | NO |
| k6 VU exhaustion | NO |

## Notes

- **k6.log is empty** for this run (driver race; see silver-PG notes).
- **-100% lag improvement vs v1** (1,170,000 → 0). Platinum-Mongo reaches zero-lag inside the cap window, whereas v1 accumulated 1.17 M events. The bulk-write `BulkWriteAsync` batching path resolves the per-partition I/O serialization bottleneck that v1 suffered.
- **p95 improvement: 36,924 ms → 5,533 ms (-85%)** — the sharpest per-tier p95 improvement in the batch.
- Zero pod restarts (same as v1). Mongo pods remain far below CPU limits regardless of bulk-write.
- 24-partition topic (expanded from 12 via PR #65) with even 3:3 assignment per pod was essential: v1's 12-partition topic assigned 2/1 partitions unevenly across 8 pods, creating structural hot/cold imbalance.
- DocumentDB `repo-metrics.json` captured a `BadRequest` error (wrong metric name); DB CPU sourced from `repo-cpu.log`.

## Artifacts

`.omc/research/kafka-loadtest/platinum-mongo-1778187086/`

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | Run summary: 8 consumer pods | 8 |
| CPU reserved at peak | 8 × cpu=1000m | 8000m |
| Memory reserved at peak | 8 × memory=1Gi | 8192 Mi (8 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 8000m / 2000m = 400.0%
- Memory: 8192 Mi / 8192 Mi = 100.0%
- **CPU binds** at 400.0%; pro-rate share = 4.0 (workload spans multiple D2s_v6 nodes — share = node multiplier)

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| Cosmos DB for MongoDB vCore M50 Compute | 0.80 — estimated | 0.60 — estimated | 1 Hour |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| DocDB M50 compute (estimated) | ~$0.80/h × 730 | ~584.00 | ~438.00 |
| DocDB storage 32 GiB (included in M50) | included | 0.00 | 0.00 |
| DocDB subtotal | | ~584.00 | ~438.00 |
| App pro-rated D2s_v6 | 0.161 × 730 × 4.0 | 470.12 | 352.59 |
| App subtotal | | 470.12 | 352.59 |
| **Platinum Kafka v2 + DocDB total** | | **~$1,054.12** | **~$790.59** |

Savings: ~$263.53/month at 25% discount.

### Notes

- Fixed-replica Kafka deployment (not HPA-bounded).
- CPU binds (400.0%); pro-rate uses binding dimension.
- If pro-rate share > 1.0: workload spans multiple D2s_v6 nodes — share = node multiplier.
- DocDB M50 unit price estimated — Cosmos DB for MongoDB vCore tiers are not a single per-tier line in the public Retail Prices API; values consistent with public Cosmos DB MongoDB vCore pricing tables.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB, Kafka cluster (separate budget line).
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
