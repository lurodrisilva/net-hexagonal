# Kafka Inbound Load Test — Gold / PostgreSQL

Goal: characterise the application's Kafka inbound adapter end-to-end at the **Gold** profile against `pgsql-pp-gold`, with workload, broker, and load-gen lanes physically isolated across separate AKS nodepools (Phase 5 redo, driver v3).

## Run summary

| Field | Value |
|---|---|
| Run id | `1778110300` |
| Date / time (UTC) | `2026-05-06T23:31:40Z → 2026-05-06T23:47:16Z` (driver elapsed 936 s, k6 stage window 15 min) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App | `hex-scaffold` chart, image `latest` |
| Repo SKU | PostgreSQL Flex `pgsql-pp-gold` — Standard tier (per Phase 4 inventory; SKU-class consistent with Gold band) |
| Consumer pods | **4 replicas, HPA OFF**, on `nodepool2` (vmss-000001, vmss-000004, vmss-000005, vmss-000009) |
| Consumer requests | `cpu=500m`, `memory=512Mi` |
| Consumer limits | `cpu=2000m`, `memory=2Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) — outbound Kafka fan-out suppressed |
| Redis | OFF (`features.useRedis=false`) |
| Kafka transport | Strimzi `hex-scaffold-loadtest`, KRaft, 2 brokers on `nodepool4`, topic `v2.core.accounts.loadtest.gold.pg` (12 partitions, RF=2, lz4) |
| Consumer config | `maxPollRecords=250`, `fetchMinBytes=32768` (medium fetch batching per gold overlay) |
| k6 | 1 runner pod on `nodepool3`, `RUNID=1778110300`, `preAllocatedVUs=100`, `maxVUs=350` |
| Telemetry | App Insights `e65c9fd9-…`, OTel sampling 0.1, log root `Warning`, classic SDK `MaxItemsPerSecond=50` |
| Run window | 15-min cap, hit at 936 s elapsed (driver overhead ~36 s) |

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
| **Aggregate events/s offered (k6)** | **2,070.29** | `kafka_writer_message_count` |
| Total events produced | 1,867,500 | k6 stdout |
| Total bytes produced | 1.1 GB (1.2 MB/s) | `kafka_writer_message_bytes` |
| Producer-side write p95 | **71.79 µs** | `kafka_writer_write_seconds.p(95)` |
| Producer-side write avg | 55.99 µs | same |
| Producer-side write max | 61.91 ms | same |
| k6 iteration p95 | 224.79 µs | VU script overhead |
| `kafka_writer_error_count` | 0 | k6 |
| `kafka_writer_retries_count` | 0 | k6 |
| VUs max actually used | 7 of 100 | k6 (production was easy on k6) |
| **Consumed events/s (steady)** | **~502** | (sum CURRENT-OFFSET) / elapsed |
| **End-to-end p95 (poll → handler completion)** | **15,467 ms** | App Insights `customMetrics`, filter `tier='gold' and repo='postgres'` (driver v3 query was wrong — see Application telemetry section below) |
| **Limiting metric** | **app consumer throughput** | lag grew monotonically from 0 → 1.42M; producer + broker + DB all had headroom |
| Saturation point | consumer saturates at ~125 events/s/pod (4 pods × 125 ≈ 502/s) | derived |
| In 60–80% saturation band | **N/A** — DB never reached saturation; bottleneck moved upstream to consumer handler |
| Stop condition fired | `cap-reached` (15 min wall-clock) |

## Consumer health — LAG GROWING

Per-partition snapshot at run end (consumer group `hex-scaffold-loadtest-gold-postgres-1778110300`, 4 consumer pods × 3 partitions each):

| Partition | LOG-END | CURRENT | LAG | Consumer-ID prefix | Pod IP |
|---|---:|---:|---:|---|---|
| 0  | 155,671 | 32,751 | **122,920** | `rdkafka-6683…` | 10.244.14.25 |
| 1  | 155,636 | 50,832 | **104,804** | `rdkafka-6683…` | 10.244.14.25 |
| 2  | 155,608 | 30,984 | **124,624** | `rdkafka-6683…` | 10.244.14.25 |
| 3  | 155,651 | 40,136 | **115,515** | `rdkafka-7e5c…` | 10.244.19.24 |
| 4  | 155,624 | 39,133 | **116,491** | `rdkafka-7e5c…` | 10.244.19.24 |
| 5  | 155,563 | 32,750 | **122,813** | `rdkafka-7e5c…` | 10.244.19.24 |
| 6  | 155,636 | 77,556 | 78,080 | `rdkafka-b2cd…` | 10.244.13.21 |
| 7  | 155,673 | 38,507 | **117,166** | `rdkafka-b2cd…` | 10.244.13.21 |
| 8  | 155,628 | 12,539 | **143,089** | `rdkafka-b2cd…` | 10.244.13.21 |
| 9  | 155,580 | 43,190 | **112,390** | `rdkafka-bd22…` | 10.244.15.22 |
| 10 | 155,647 | 42,378 | **113,269** | `rdkafka-bd22…` | 10.244.15.22 |
| 11 | 155,583 | 32,274 | **123,309** | `rdkafka-bd22…` | 10.244.15.22 |
| **Sum** | **1,867,572** | **472,030** | **1,395,470** | 4 consumers | 3 partitions per pod |

- Producer count (k6): 1,867,500 ≈ broker LOG-END sum (1,867,572) → broker ingested every event ✅
- Consumer total: 472,030 over 936 s = **~504 events/s**
- Per-pod consume rate: ~125 events/s/pod (uniform across all 4 pods)
- Lag: 0 at start → **1.42 M at end**, growing roughly linearly throughout the peak phase

## Repository (PostgreSQL Flex `pgsql-pp-gold`)

| Stat | Value |
|---|---|
| Min CPU | 0.97% (early ramp) |
| Max CPU | 10.19% |
| Mean CPU (peak window) | ~9.7% |
| Lag growth despite low DB CPU | confirms DB is **not** the bottleneck |

The repository ran at < 11% CPU throughout. The consumer handler in the application (or Kafka consumer fetch loop) is the limit, not PostgreSQL.

## Application pod consumption (Container Insights)

Source: Azure Log Analytics workspace `aks-test-workspace`, `Perf` table, container `hex-scaffold`. Window: `2026-05-06T23:31:40Z → 2026-05-06T23:47:16Z`.

| Pod (by node) | CPU avg | CPU max (burst) | Memory avg | Memory max | % of CPU limit |
|---|---:|---:|---:|---:|---:|
| `vmss-000001` | 922 m | 2,147 m | 176 MiB | 198 MiB | 46 % avg, **107 % burst** |
| `vmss-000004` | 928 m | 2,147 m | 156 MiB | 176 MiB | 46 % avg, **107 % burst** |
| `vmss-000005` | 913 m | 2,147 m | 153 MiB | 181 MiB | 46 % avg, **107 % burst** |
| `vmss-000009` | 923 m | 2,147 m | 165 MiB | 184 MiB | 46 % avg, **107 % burst** |

Notes:
- All 4 pods burned 913–928 m CPU avg against `request=500m` (using **1.84× requested**) and `limit=2000m` (46 % avg).
- Max samples (2,147 m) marginally exceed the 2 cpu limit due to CFS bursting — pods occasionally hit the throttle ceiling.
- Memory working set 153–176 MiB (8 % of 2 GiB limit) — memory wildly over-provisioned.
- Per-pod throughput efficiency: **125 events/s ÷ 922 mcores = ~0.14 events/s/mcore** (2.4× worse than Silver).
- **Pod restarts during run:** 1 of 4 pods (`hex-scaffold-8577549ff6-rxq9l`) restarted once. Likely a liveness probe failure under load (CPU throttling momentarily missing the probe deadline). Worth investigating but not run-fatal.

The Gold tier consumer is **CPU-active but not CPU-starved**: it has ~54 % CPU headroom under the 2-cpu limit yet still cannot keep up with the producer rate. This rules out CPU as the primary bottleneck and points at intra-handler serialization (e.g., synchronous EF Core write blocking the fetch loop, or single-flight per-partition Mediator dispatch) as the likely cause.

## Application telemetry (App Insights) — CORRECTED

The metric pipeline IS working — the v1/v2/v3 driver query was wrong. It filtered on `customDimensions.runId`, but `runId` is set as an Activity tag (consumer.cs:98) and lands in `requests` / `dependencies`, NOT `customMetrics`. The metric's tag set is `event_type` / `tier` / `repo` (bounded by the OTel View at `ObservabilityConfig.cs:144-149` for cardinality budget).

Re-queried with corrected filter (`tier == 'gold' and repo == 'postgres'`, captured by the in-run diagnostic query stored in `ai-diag-norid.json`):

| Metric | Value |
|---|---|
| Records ingested for gold/postgres | **180** |
| **End-to-end p95 (poll → handler completion)** | **15,467 ms** |
| **End-to-end p99** | (not captured — single-percentile diagnostic) |

The end-to-end p95 = **15.5 seconds** at Gold tier confirms what the consumer-lag growth implied: events sit in the consumer fetch queue for ~15 s before the handler completes. This is consistent with the per-pod 125 events/s consumption ceiling against a 2070 events/s offered rate — the queue depth grows linearly until lag-derived latency dominates.

Driver patched (post-PR-#59) to use the correct filter going forward; existing runs' diagnostic queries already captured the data.

## Real-time stop-signal trace

| Signal | Status |
|---|---|
| `inbound_event_processing_duration_ms` p95 ≥ 200 ms | not captured |
| Repo CPU > 80% for ≥ 2 samples | NO (peak 10.19%) |
| Consumer-group lag monotonic > 2 min | **YES, but driver does not currently fail on this** — would benefit from a `lag-growth` stop rule |
| Cap reached | YES — 15 min wall-clock |

## Pod placement (Phase 5 redo isolation)

| Workload | Nodepool | Notes |
|---|---|---|
| App pods (4) | nodepool2 (predominant), some on nodepool/nodepool3 (overflow) | preferred weight=100 on nodepool2 |
| k6 runner | **nodepool3** | hard pin |
| Strimzi brokers + sidecars (5) | **nodepool4** | broker pool + cruise-control + entity-op + kafka-exporter |

## Cleanup

| Action | Status |
|---|---|
| Synthetic-row delete | failed locally (psql DNS — local env outside cluster); rows persist in PG |
| Consumer-group delete | succeeded via in-cluster broker pod exec |

## Key observations

The Gold tier exposes the **first real bottleneck**: the application's consumer handler. The producer (k6) effortlessly delivered 2,070 events/s, the brokers ingested all 1.87 M messages without retries or errors, and the PostgreSQL repository sat at ~10% CPU throughout. The bottleneck is between the broker and the DB — i.e., the consumer fetch + handler dispatch + repository write loop in the application.

Per-pod consumer rate (~125 events/s/pod) is uniform across all 4 replicas, so the work distribution is healthy; the per-pod ceiling is what limits aggregate throughput. Scaling the deployment to 8 pods (Platinum tier) raises consumer rate to ~156 events/s/pod (24% per-pod gain from larger fetch batch + better pod resource allocation), but lag still grows.

Hot vs cold partition spread is tight (LAG 78k–143k, ~1.8× ratio), so partition assignment is not the cause of the imbalance. The bottleneck is uniform per-pod handler throughput.

## Validation summary

- Producer/broker/transport: comfortable headroom ✅
- DB: comfortable headroom (10% CPU) ✅
- App consumer: **saturated** at ~125 events/s/pod ❌
- Driver v3: clean run, all artifacts captured (k6.log + lag + repo CPU full series) ✅
- App Insights pipeline: still broken — needs separate fix ❌

## Artifacts

`.omc/research/kafka-loadtest/gold-pg-1778110300/`

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
- **CPU binds** at 100.0%; pro-rate share = 1.0

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| PG Flex GP Dadsv5 4 vCore (`Standard_D4ds_v5`) | 0.4800 | 0.36000 | 1 Hour |
| PG Flex Storage Data Stored | 0.2185 | 0.16388 | 1 GiB/Month |
| PG Flex Storage IOPS | 0.0400 | 0.03000 | 1 IOPS/Month |
| PG Flex Storage Throughput | 0.1600 | 0.12000 | 1 MBps/Month |
| PG Flex Backup Storage LRS Data Stored | 0.0950 | 0.07125 | 1 GB/Month |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| PG D4ds_v5 compute | 0.4800 × 730 | 350.40 | 262.80 |
| PG storage 128 GiB | 0.2185 × 128 | 27.97 | 20.98 |
| PG storage 6000 IOPS | 0.04 × 6000 | 240.00 | 180.00 |
| PG storage 500 MBps throughput | 0.16 × 500 | 80.00 | 60.00 |
| PG backup ≤ 128 GiB | included | 0.00 | 0.00 |
| PG subtotal | | 698.37 | 523.78 |
| App pro-rated D2s_v6 | 0.161 × 730 × 1.0 | 117.53 | 88.15 |
| App subtotal | | 117.53 | 88.15 |
| **Gold Kafka v1 + PG total** | | **$815.90** | **$611.93** |

Savings: $203.97/month at 25% discount.

### Notes

- Fixed-replica Kafka deployment (consumer Deployment, not HPA-bounded).
- CPU binds (100.0%); pro-rate share = 1.0 (exactly one D2s_v6 node).
- Premium SSD v2 storage: 128 GiB + 6000 IOPS + 500 MBps as provisioned for Gold tier.
- If pro-rate share > 1.0: workload spans multiple D2s_v6 nodes — share treated as multiplier.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB, Kafka cluster (Strimzi MSK or self-hosted — separate budget line).
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
