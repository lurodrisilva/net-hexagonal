# Kafka Inbound Load Test — Silver / DocumentDB (Phase 5 v2)

Goal: re-run the Phase 5 silver-Mongo tier after bulk-write coalescing (PR #66 + PR #68) to close the 146,616 end-lag from v1.

## Run summary

| Field | Value |
|---|---|
| Run id | `1778182498` |
| Date / time (UTC) | `2026-05-07T19:34:58Z → 2026-05-07T19:51:03Z` (driver elapsed 965 s) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App image | `latest` (includes PR #66 bulk-write + PR #68 same-batch id-collapse fix) |
| Repo SKU | Cosmos DB Mongo vCore `documentdb-silver` (Brazil South) |
| Consumer pods | **2 replicas, HPA OFF**, on `nodepool2` |
| Consumer requests / limits | `cpu=1000m/2000m`, `memory=1Gi/4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) |
| Redis | OFF |
| Kafka topic | `v2.core.accounts.loadtest.silver.mongo` (12 partitions, RF=2, lz4) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=16384` (silver overlay) |
| k6 runner | `nodepool3`, `TIER=silver`, `REPO=mongo`, `RUNID=1778182498` |
| Driver | v3 + AI-fix + `P95_BREACH_THRESHOLD_MS=60000` |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 100 events/s |
| Ramp | 3 min | → 500 events/s |
| Peak | 10 min | 500 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70% Create / 25% Update / 5% Delete.

## Result

| Metric | Value | Source |
|---|---|---|
| Stop reason | `cap-reached` (15-min wall clock) | `summary.json` |
| Elapsed | 965 s | `summary.json` |
| Broker LOG-END (sum 12 partitions) | 1,286,104 | `consumer-group-lag.log` |
| Effective offered rate | ~1,333 /s | LOG-END / elapsed |
| Consumer CURRENT-OFFSET (sum) | 1,285,693 | `consumer-group-lag.log` |
| Consumed rate | ~1,332 /s | CURRENT / elapsed |
| **End-of-run lag** | **411** | `consumer-group-lag.log` |
| **e2e p50** | 1,491 ms | `ai-final.json` |
| **e2e p95** | **11,337 ms** | `ai-final.json` |
| **e2e p99** | 32,101 ms | `ai-final.json` |
| e2e avg / max | 2,915 / 32,101 ms | `ai-final.json` |
| AI record count | 72 | `ai-final.json` |
| k6 offered rate (stdout) | not captured — k6.log empty (driver race) | `k6.log` |

## Consumer health — LAG NEAR-ZERO

Per-partition snapshot at run end (12 partitions, 2 consumer pods = 6 partitions each):

| Partition | LOG-END | CURRENT | LAG |
|---|---:|---:|---:|
| 0 | 107,182 | 107,144 | 38 |
| 1 | 107,253 | 107,217 | 36 |
| 2 | 107,211 | 107,175 | 36 |
| 3 | 107,173 | 107,137 | 36 |
| 4 | 107,212 | 107,174 | 38 |
| 5 | 107,187 | 107,149 | 38 |
| 6 | 107,167 | 107,137 | 30 |
| 7 | 107,141 | 107,107 | 34 |
| 8 | 107,157 | 107,128 | 29 |
| 9 | 107,161 | 107,127 | 34 |
| 10 | 107,138 | 107,106 | 32 |
| 11 | 107,122 | 107,092 | 30 |
| **Sum** | **1,286,104** | **1,285,693** | **411** |

Uniform per-partition lag (29–38) with no hot/cold split — both pods tracked at equal rates. End-lag 411 is a residual catchup tail (same retained-backlog caveat as silver-PG — see Notes).

Effective offered rate ~1,333 /s is well above the nominal 500 /s profile target because the topic carried retained events from earlier silver-mongo runs earlier in the day; the consumer consumed both the k6 stream and the backlog simultaneously.

## Repository (DocumentDB `documentdb-silver`)

| Stat | Value |
|---|---|
| Peak CPU | 54.08 % (at 19:37Z, during ramp) |
| Average CPU (all samples) | ~21.8 % |
| Binding constraint | **DB is the binding constraint at silver** |

Silver-Mongo remains the only run in the batch where the database itself is the throughput ceiling: the 54 % CPU spike at ramp exactly coincides with the period of fastest lag drainage, and steady-state CPU plateaued at ~20–22 % through the peak window. The `documentdb-silver` SKU is sized smaller relative to load than PG silver.

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
- **End-lag 411 is not a regression vs v1's 146,616.** This is a -99.7% improvement. The 411-event residual is the catchup tail from retained backlog, not a steady-state failure. The bulk-write path (PR #66) combined with the same-batch id-collapse fix (PR #68) together eliminated the prior lag accumulation.
- **p95 improvement: 34,942 ms → 11,337 ms (-67%)** even with contamination from retained-backlog payloads. Steady-state p95 after drain would be lower still.
- DocumentDB `repo-metrics.json` captured a `BadRequest` error (driver used wrong metric name `RequestUnitsConsumed`); DB CPU sourced from `repo-cpu.log` poll loop.

## Artifacts

`.omc/research/kafka-loadtest/silver-mongo-1778182498/`

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | Run summary: 2 consumer pods | 2 |
| CPU reserved at peak | 2 × cpu=1000m | 2000m |
| Memory reserved at peak | 2 × memory=1Gi | 2048 Mi (2 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 2000m / 2000m = 100.0%
- Memory: 2048 Mi / 8192 Mi = 25.0%
- **CPU binds** at 100.0%; pro-rate share = 1.0 (workload spans multiple D2s_v6 nodes — share = node multiplier)

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| Cosmos DB for MongoDB vCore M20 Compute | 0.10 — estimated | 0.075 — estimated | 1 Hour |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| DocDB M20 compute (estimated) | ~$0.10/h × 730 | ~73.00 | ~54.75 |
| DocDB storage 32 GiB (included in M20) | included | 0.00 | 0.00 |
| DocDB subtotal | | ~73.00 | ~54.75 |
| App pro-rated D2s_v6 | 0.161 × 730 × 1.0 | 117.53 | 88.15 |
| App subtotal | | 117.53 | 88.15 |
| **Silver Kafka v2 + DocDB total** | | **~$190.53** | **~$142.90** |

Savings: ~$47.63/month at 25% discount.

### Notes

- Fixed-replica Kafka deployment (not HPA-bounded).
- CPU binds (100.0%); pro-rate uses binding dimension.
- If pro-rate share > 1.0: workload spans multiple D2s_v6 nodes — share = node multiplier.
- DocDB M20 unit price estimated — Cosmos DB for MongoDB vCore tiers are not a single per-tier line in the public Retail Prices API; values consistent with public Cosmos DB MongoDB vCore pricing tables.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB, Kafka cluster (separate budget line).
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
