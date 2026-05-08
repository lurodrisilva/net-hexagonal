# Kafka Inbound Load Test — Gold / DocumentDB (Mongo vCore)

Goal: characterise the application's Kafka inbound adapter end-to-end at the **Gold** profile against `documentdb-gold` (Cosmos DB Mongo vCore), with workload, broker, and load-gen lanes physically isolated across separate AKS nodepools (Phase 5 redo, driver v3 + AI-fix).

## Run summary

| Field | Value |
|---|---|
| Run id | `1778121207` |
| Date / time (UTC) | `2026-05-07T02:33:27Z → 2026-05-07T02:49:37Z` (driver elapsed 965 s, k6 stage window 15 min) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App | `hex-scaffold` chart, image `latest` |
| Repo SKU | Cosmos DB Mongo vCore `documentdb-gold` (Brazil South), shared admin user `dbuser`, SCRAM-SHA-256 |
| Consumer pods | **4 replicas, HPA OFF**, on `nodepool2` (vmss-000001, 000007, 000008, 000009) |
| Consumer requests | `cpu=500m`, `memory=512Mi` |
| Consumer limits | `cpu=2000m`, `memory=2Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) — outbound Kafka fan-out suppressed |
| Redis | OFF (`features.useRedis=false`) |
| Kafka transport | Strimzi `hex-scaffold-loadtest`, KRaft, 2 brokers on `nodepool4`, topic `v2.core.accounts.loadtest.gold.mongo` (12 partitions, RF=2, lz4) |
| Consumer config | `maxPollRecords=250`, `fetchMinBytes=32768` (medium fetch batching per gold overlay) |
| k6 | 1 runner pod on `nodepool3`, `RUNID=1778121207`, `preAllocatedVUs=100`, `maxVUs=350` |
| Telemetry | App Insights `e65c9fd9-…`, OTel sampling 0.1, log root `Warning`, classic SDK `MaxItemsPerSecond=50` |
| Driver overrides | `P95_BREACH_THRESHOLD_MS=60000` (raised from 200 ms) so the run reaches 15-min cap |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 500 events/s |
| Ramp | 3 min | → 2500 events/s |
| Peak | 10 min | 2500 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70% Created / 25% Updated / 5% Deleted.

## Result

| Metric | Value | Source |
|---|---|---|
| **Aggregate events/s offered (k6)** | **2,070.24** | `kafka_writer_message_count` |
| Total events produced | 1,867,499 | k6 stdout |
| Total bytes produced | 1.1 GB (1.2 MB/s) | `kafka_writer_message_bytes` |
| Producer-side write p95 | **69.1 µs** | `kafka_writer_write_seconds.p(95)` |
| Producer-side write avg | 52.4 µs | same |
| Producer-side write max | 61.9 ms | same |
| k6 iteration p95 | 215 µs | VU script overhead |
| `kafka_writer_error_count` | 0 | k6 |
| `kafka_writer_retries_count` | 0 | k6 |
| VUs max actually used | 4 of 100 | k6 |
| **Consumed events/s (steady)** | **~447** | (sum CURRENT-OFFSET) / elapsed |
| **End-to-end p95 (poll → handler completion)** | **29,689 ms** | App Insights `customMetrics`, filter `tier='gold' and repo='mongo'`, 2 h post-cap window |
| **End-to-end p99** | **33,742 ms** | same |
| End-to-end p50 / max | 5,282 / 35,589 ms | same |
| Limiting metric | **app consumer handler — DocumentDB write path I/O-bound** | derived |
| Saturation point | per-pod consumer rate ~112/s (4 pods × 112 ≈ 447/s aggregate) | derived |
| In 60–80 % saturation band | NO — DocDB CPU peaked 18.6 %; bottleneck is upstream in handler I/O wait | repo-cpu.log |
| Stop condition fired | `cap-reached` (15 min wall-clock) |

## Consumer health — LAG GROWING

Per-partition snapshot at run end (consumer group `hex-scaffold-loadtest-gold-mongo-1778121207`, 4 consumer pods × 3 partitions each):

| Partition | LOG-END | CURRENT | LAG | Pod IP |
|---|---:|---:|---:|---|
| 0  | 159,711 | 33,137 | **126,574** | 10.244.10.23 |
| 1  | 159,438 | 43,495 | **115,943** | 10.244.10.23 |
| 2  | 159,648 | 33,148 | **126,500** | 10.244.10.23 |
| 3  | 159,631 | 32,261 | **127,370** | 10.244.18.32 |
| 4  | 159,557 | 30,424 | **129,133** | 10.244.18.32 |
| 5  | 159,887 | 38,730 | **121,157** | 10.244.18.32 |
| 6  | 159,672 | 43,070 | **116,602** | 10.244.15.26 |
| 7  | 159,718 | 31,619 | **128,099** | 10.244.15.26 |
| 8  | 159,635 | 43,095 | **116,540** | 10.244.15.26 |
| 9  | 159,706 | 35,090 | **124,616** | 10.244.14.28 |
| 10 | 159,639 | 34,617 | **125,022** | 10.244.14.28 |
| 11 | 159,683 | 32,813 | **126,870** | 10.244.14.28 |
| **Sum** | **1,915,925** | **431,499** | **1,484,426** | 4 consumers |

- Producer count (k6): 1,867,499 ≈ broker LOG-END sum (1,915,925 − retention from earlier same-topic runs) — broker ingested every event ✅
- Consumer total: 431,499 over 965 s = **~447 events/s consumed**
- Per-pod consume rate: ~112 events/s/pod (uniform across all 4 pods, ±5 %)
- Lag at end: **1.48 M** (vs PG gold: 1.42 M) — both gold tiers are handler-bound at ~125 events/s/pod
- Hot vs cold partition spread: 129k max vs 116k min = **1.1× imbalance** — partition assignment perfectly even

## Repository (DocumentDB Mongo vCore `documentdb-gold`)

| Stat | Value |
|---|---|
| Min CPU | 2.7 % (early ramp) |
| Max CPU | 18.6 % |
| Mean CPU (peak window) | ~16 % |
| Lag growth despite low DB CPU | confirms DocDB is **not** the bottleneck at gold |

DocumentDB-gold is provisioned big enough to absorb 447 events/s comfortably (peak 18.6% CPU). The consumer handler in the application (Mongo C# driver write loop) is the limit, not the DocDB cluster.

## Application pod consumption (Container Insights)

Source: Azure Log Analytics workspace `aks-test-workspace`, `Perf` table, container `hex-scaffold`. Window: `2026-05-07T02:33:27Z → 2026-05-07T02:49:37Z`.

| Pod (by node) | CPU avg | CPU max (burst) | Memory avg | Memory max | % of CPU limit |
|---|---:|---:|---:|---:|---:|
| `vmss-000001` | 124 m | 209 m | 124 MiB | 165 MiB | 6 % avg, 10 % burst |
| `vmss-000007` | 120 m | 204 m | 120 MiB | 167 MiB | 6 % avg, 10 % burst |
| `vmss-000008` | 136 m | 313 m | 136 MiB | 160 MiB | 7 % avg, 16 % burst |
| `vmss-000009` | 160 m | 379 m | 160 MiB | 173 MiB | 8 % avg, 19 % burst |

Notes:
- All 4 pods burned **120–160 m CPU avg** against `request=500m` (using **0.27× requested**) and `limit=2000m` (6–8 % avg). Pods are massively over-provisioned vs actual handler CPU need.
- Max samples (204–379 m) sit far below the 2 cpu limit — no throttling.
- Memory working set 120–160 MiB (6–8 % of 2 GiB limit) — wildly over-provisioned.
- Per-pod throughput efficiency: **112 events/s ÷ 135 mcores = ~0.83 events/s/mcore** (vs PG gold 0.14, vs Mongo silver 2.32). Mongo gold per-mcore = 5.9× PG gold per-mcore — but the work is offloaded to DocDB's I/O latency, not local CPU.
- **Pod restarts during run:** 0 of 4 (vs PG gold: 1). Consumer threads block on Mongo client awaiting acks — they never miss probe deadlines because they're not CPU-starved.

The Mongo gold consumer is **CPU-idle, I/O-blocked on DocDB Mongo vCore commits**: pods sit at ~7 % of their CPU limit while lag grows 1.48 M events. Throughput ceiling per pod is set by Mongo client thread lifecycle (likely a single async write per partition consumer thread), not handler CPU cost.

## Application telemetry (App Insights)

Filter `tier == 'gold' and repo == 'mongo'`, 2 h post-cap window:

| Metric | Value |
|---|---:|
| Records ingested for gold/mongo | 234 |
| **End-to-end p50** | 5,282 ms |
| **End-to-end p95** | **29,689 ms** |
| **End-to-end p99** | 33,742 ms |
| End-to-end max | 35,589 ms |

Compared to **PG gold** (p95 15.5 s): Mongo gold is **1.9× slower end-to-end**. Both runs offered the same 2,070 events/s and both ran 4 pods, but Mongo's per-pod write throughput (~112/s) is 11 % lower than PG's (~125/s).

## Real-time stop-signal trace

| Signal | Status |
|---|---|
| `inbound_event_processing_duration_ms` p95 ≥ 60,000 ms | NO (peak ~36 s) |
| Repo CPU > 80 % for ≥ 2 samples | NO (peak 18.6 %) |
| Consumer-group lag monotonic > 2 min | **YES** — driver does not currently fail on this |
| Cap reached | YES — 15 min wall-clock |

## Pod placement (Phase 5 redo isolation)

| Workload | Nodepool | Notes |
|---|---|---|
| App pods (4) | nodepool2 (vmss 01/07/08/09) | preferred weight=100 on nodepool2 |
| k6 runner | **nodepool3** | hard pin |
| Strimzi brokers + sidecars (5) | **nodepool4** | broker pool + cruise-control + entity-op + kafka-exporter |

## Cleanup

| Action | Status |
|---|---|
| Synthetic-document delete | succeeded via timestamp predicate |
| Consumer-group delete | succeeded via in-cluster broker pod exec |

## Key observations

The Gold Mongo tier behaves nearly identically to Gold PG at the per-pod level (~112 vs ~125 events/s consumed), but the underlying limit differs: PG gold is CPU-active (922 m avg) while Mongo gold is CPU-idle (135 m avg). Both block on the same upstream symptom (single per-partition consumer thread waits for write to complete), but the wait time per event for Mongo (~9 ms per write) is materially longer than PG's (~8 ms per write + actual EF Core CPU spent).

Per-pod consumer rate (~112 events/s/pod) is uniform across all 4 replicas, so the work distribution is healthy; the per-pod ceiling is what limits aggregate throughput. Adding pods would not help because partition count = 12 caps consumer thread parallelism at 12 (3 per pod). The Mongo gold lever set is the same as Mongo silver: bigger DocDB SKU + bulk write coalescing + more partitions.

Hot vs cold partition spread is tight (1.1× ratio), so partition assignment is not the cause of lag growth. The bottleneck is uniform per-pod handler I/O wait.

## Validation summary

- Producer/broker/transport: comfortable headroom (69 µs p95) ✅
- DocDB: comfortable headroom (18.6 % CPU) ✅
- App consumer: **I/O-blocked at ~112 events/s/pod** ❌
- Driver v3 + AI-fix: clean run, all artifacts captured ✅

## Artifacts

`.omc/research/kafka-loadtest/gold-mongo-1778121207/`

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | Run summary: 4 consumer pods | 4 |
| CPU reserved at peak | 4 × cpu=500m | 2000m |
| Memory reserved at peak | 4 × memory=512Mi | 2048 Mi (2 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 2000m / 2000m = 100.0%
- Memory: 2048 Mi / 8192 Mi = 25.0%
- **CPU binds** at 100.0%; pro-rate share = 1.0 (workload spans multiple D2s_v6 nodes — share = node multiplier)

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| Cosmos DB for MongoDB vCore M30 Compute | 0.20 — estimated | 0.15 — estimated | 1 Hour |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| DocDB M30 compute (estimated) | ~$0.20/h × 730 | ~146.00 | ~109.50 |
| DocDB storage 32 GiB (included in M30) | included | 0.00 | 0.00 |
| DocDB subtotal | | ~146.00 | ~109.50 |
| App pro-rated D2s_v6 | 0.161 × 730 × 1.0 | 117.53 | 88.15 |
| App subtotal | | 117.53 | 88.15 |
| **Gold Kafka v1 + DocDB total** | | **~$263.53** | **~$197.65** |

Savings: ~$65.88/month at 25% discount.

### Notes

- Fixed-replica Kafka deployment (not HPA-bounded).
- CPU binds (100.0%); pro-rate uses binding dimension.
- If pro-rate share > 1.0: workload spans multiple D2s_v6 nodes — share = node multiplier.
- DocDB M30 unit price estimated — Cosmos DB for MongoDB vCore tiers are not a single per-tier line in the public Retail Prices API; values consistent with public Cosmos DB MongoDB vCore pricing tables.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB, Kafka cluster (separate budget line).
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
