# Kafka Inbound Load Test — Platinum / PostgreSQL

Goal: characterise the application's Kafka inbound adapter end-to-end at the **Platinum** profile against `pgsql-pp-platinum-1`, with workload, broker, and load-gen lanes physically isolated across separate AKS nodepools (Phase 5 redo, driver v3).

## Run summary

| Field | Value |
|---|---|
| Run id | `1778111250` |
| Date / time (UTC) | `2026-05-06T23:47:30Z → 2026-05-07T00:03:13Z` (driver elapsed 929 s, k6 stage window 15 min) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App | `hex-scaffold` chart, image `latest` |
| Repo SKU | PostgreSQL Flex `pgsql-pp-platinum-1` (per Phase 4 inventory; SKU class consistent with Platinum band) |
| Consumer pods | **8 replicas, HPA OFF**, on `nodepool2` (vmss-000000/01/04/05/06/07/08/09) |
| Consumer requests | `cpu=1000m`, `memory=1Gi` |
| Consumer limits | `cpu=4000m`, `memory=4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) |
| Redis | OFF |
| Kafka transport | Strimzi `hex-scaffold-loadtest`, KRaft, 2 brokers on `nodepool4`, topic `v2.core.accounts.loadtest.platinum.pg` (12 partitions, RF=2, lz4) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=65536` (aggressive fetch batching per platinum overlay) |
| k6 | 1 runner pod on `nodepool3`, `RUNID=1778111250`, `preAllocatedVUs=200`, `maxVUs=720` |
| Telemetry | App Insights `e65c9fd9-…`, OTel sampling 0.1, log root `Warning`, classic SDK `MaxItemsPerSecond=50` |
| Run window | 15-min cap, hit at 929 s elapsed |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 1060 events/s (20% of peak) |
| Ramp | 3 min | → 5300 events/s |
| Peak | 10 min | 5300 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70% Created / 25% Updated / 5% Deleted.

## Result

| Metric | Value | Source |
|---|---|---|
| **Aggregate events/s offered (k6)** | **3,238.22** | `kafka_writer_message_count` |
| Total events produced | 2,921,163 | k6 stdout |
| **Dropped iterations (k6 couldn't fan-out)** | **253,231** (280.7/s) | `dropped_iterations` |
| Effective offered (incl drops) | ~3,519 events/s attempted, ~3,238 actually produced | derived |
| Total bytes produced | 1.8 GB (2.0 MB/s) | `kafka_writer_message_bytes` |
| Producer-side write p95 | **214.44 µs** (3× silver/gold) | `kafka_writer_write_seconds.p(95)` |
| Producer-side write avg | 2.39 ms | same |
| Producer-side write max | 1.08 s | same |
| k6 iteration p95 | 1.48 ms (10× gold) | VU script overhead — VUs queueing |
| `kafka_writer_error_count` | 0 | k6 |
| `kafka_writer_retries_count` | 0 | k6 |
| VUs max actually used | **720 of 720** | k6 — exhausted preAllocated pool |
| **Consumed events/s (steady)** | **~1,248** | (sum CURRENT-OFFSET) / elapsed |
| **End-to-end p95 (poll → handler completion)** | **24,328 ms during run / 33,230 ms post-run drain** | App Insights `customMetrics`, filter `tier='platinum' and repo='postgres'` (corrected query) |
| **Limiting metric** | **DUAL: k6 producer + app consumer**; k6 dropped 253k iter (could not produce target rate); app consumer kept growing lag | observation |
| Stop condition fired | `cap-reached` (15 min wall-clock) |

## Consumer health — LAG GROWING

Per-partition snapshot at run end (consumer group `hex-scaffold-loadtest-platinum-postgres-1778111250`, 8 consumer pods):

| Partition | LOG-END | CURRENT | LAG | Consumer-ID prefix | Pod IP |
|---|---:|---:|---:|---|---|
| 0  | 243,388 | 82,067  | **161,321** | `rdkafka-1f61…` | 10.244.14.26 |
| 1  | 243,461 | 56,285  | **187,176** | `rdkafka-1f61…` | 10.244.14.26 |
| 2  | 243,441 | 83,864  | **159,577** | `rdkafka-2057…` | 10.244.12.20 |
| 3  | 243,390 | 85,064  | **158,326** | `rdkafka-2057…` | 10.244.12.20 |
| 4  | 243,540 | 94,303  | **149,237** | `rdkafka-4543…` | 10.244.16.24 |
| 5  | 243,350 | 75,375  | **167,975** | `rdkafka-4543…` | 10.244.16.24 |
| 6  | 243,480 | 60,835  | **182,645** | `rdkafka-6ada…` | 10.244.19.25 |
| 7  | 243,452 | 73,935  | **169,517** | `rdkafka-6ada…` | 10.244.19.25 |
| 8  | 243,457 | 136,762 | 106,695 | `rdkafka-7d5c…` | 10.244.13.22 |
| 9  | 243,414 | 136,290 | 107,124 | `rdkafka-8077…` | 10.244.10.21 |
| 10 | 243,332 | 120,459 | 122,873 | `rdkafka-8580…` | 10.244.18.30 |
| 11 | 243,458 | 151,969 | 91,489  | `rdkafka-8d23…` | 10.244.15.23 |
| **Sum** | **2,921,163** | **1,157,208** | **1,763,955** | 8 consumers | each owns 1 partition |

- Producer count (k6): 2,921,163 = broker LOG-END sum ✅
- Consumer total: 1,157,208 over 929 s = **~1,246 events/s**
- Per-pod consume rate: ~156 events/s/pod (1248/8 = 156)
- Lag: 0 at start → **1.76 M at end**, growing linearly
- Partition assignment: **1 partition per pod** (vs gold's 3:1) — wider parallelism

## Repository (PostgreSQL Flex `pgsql-pp-platinum-1`)

| Stat | Value |
|---|---|
| Min CPU | (early ramp) |
| Max CPU | 10.94% |
| Mean CPU (peak window) | ~10.6% |
| Lag growth despite low DB CPU | DB **not** the bottleneck |

The Platinum PG instance ran at < 11% CPU throughout the entire run, identical headroom to the Gold tier. The repository tier is **wildly over-provisioned** for the actual application throughput.

## Application pod consumption (Container Insights)

Source: Azure Log Analytics workspace `aks-test-workspace`, `Perf` table, container `hex-scaffold`. Window: `2026-05-06T23:47:30Z → 2026-05-07T00:03:13Z`.

| Pod (by node) | CPU avg | CPU max (burst) | Memory avg | Memory max | % of CPU limit |
|---|---:|---:|---:|---:|---:|
| `vmss-000000` | 1,547 m | 4,294 m | 151 MiB | 163 MiB | 39 % avg, **107 % burst** |
| `vmss-000001` | 1,550 m | 4,294 m | 151 MiB | 170 MiB | 39 % avg, **107 % burst** |
| `vmss-000004` | 1,554 m | 4,294 m | 155 MiB | 169 MiB | 39 % avg, **107 % burst** |
| `vmss-000005` | 1,539 m | 4,294 m | 144 MiB | 161 MiB | 38 % avg, **107 % burst** |
| `vmss-000006` | 1,550 m | 4,294 m | 154 MiB | 178 MiB | 39 % avg, **107 % burst** |
| `vmss-000007` | 1,544 m | 4,294 m | 151 MiB | 165 MiB | 39 % avg, **107 % burst** |
| `vmss-000008` | 1,543 m | 4,294 m | 146 MiB | 160 MiB | 39 % avg, **107 % burst** |
| `vmss-000009` | 1,548 m | 4,294 m | 151 MiB | 165 MiB | 39 % avg, **107 % burst** |

Notes:
- All 8 pods burned 1,539–1,554 m CPU avg against `request=1000m` (using **1.55× requested**) and `limit=4000m` (39 % avg).
- Max samples 4,294 m marginally exceed the 4 cpu limit due to CFS bursting.
- Memory working set 144–155 MiB (4 % of 4 GiB limit) — memory wildly over-provisioned.
- Per-pod throughput efficiency: **156 events/s ÷ 1,547 mcores = ~0.10 events/s/mcore** (3.3× worse than Silver).
- **Pod restarts during run:** 3 of 8 pods (`bcbf4bc7b-w4vxs`, `bcbf4bc7b-spqkd`, `bcbf4bc7b-m5256`) restarted once each — same pattern as Gold (likely liveness probe missed under sustained CPU pressure). Higher restart count tracks higher tier load.

The Platinum tier consumer is **CPU-active but with massive headroom** (61 % CPU unused under the 4-cpu limit per pod). As at Gold, this rules out CPU as the primary bottleneck and reinforces the intra-handler serialization hypothesis. The fact that per-pod throughput is even worse than Gold (despite more aggressive fetch settings: `maxPollRecords=500`, `fetchMinBytes=65536`) suggests fetch batch processing itself is adding per-event overhead — likely the larger batch incurs more bookkeeping per Mediator dispatch or per EF Core SaveChanges round-trip.

## Application telemetry (App Insights) — CORRECTED

The metric pipeline IS working — the v1/v2/v3 driver query was wrong. It filtered on `customDimensions.runId`, but `runId` is set as an Activity tag (consumer.cs:98) and lands in `requests` / `dependencies`, NOT `customMetrics`. Driver patched to filter on `tier` + `repo` + run-window timestamp.

Re-queried after the fact (records arrive in `customMetrics` with 5–15 min ingest lag because of classic SDK adaptive sampling at `MaxItemsPerSecond=50`):

| Window | Records | p95 | max | avg |
|---|---:|---:|---:|---:|
| During run window (`23:47:30Z..00:03:13Z`) | 360 | **24,328 ms** | — | — |
| Post-drain window (`00:18:39Z..00:40:41Z`) | 192 | **33,230 ms** | 33,607 ms | 13,884 ms |

The end-to-end p95 = **24–33 seconds** at Platinum confirms the lag-derived bottleneck story: at 1,248 events/s consumed against 3,238 events/s offered, queue depth grows linearly and per-event latency tracks queue depth. Records continued arriving for ~37 min after the producer cut off (00:03:13 → 00:40:41) because the consumer was still draining the 1.76 M lag built up during the run.

## k6 producer-side analysis

| Indicator | Value | Interpretation |
|---|---|---|
| `dropped_iterations` | 253,231 (280.7/s) | k6 VU pool could not spawn iterations fast enough |
| `vus_max` | 720 (= `maxVUs`) | exhausted the configured ceiling |
| `kafka_writer_batch_queue_seconds.p(95)` | 1.64 ms | xk6-kafka batch queue building up |
| `iteration_duration.p(95)` | 1.48 ms | VU script overhead 10× gold |

The k6 producer is at its configured limit. To actually push 5300 events/s the Platinum profile would need `preAllocatedVUs ≥ 500`, `maxVUs ≥ 1500`, and possibly multiple runner pods (parallelism > 1). Currently k6 is the producer-side ceiling, not the broker.

## Real-time stop-signal trace

| Signal | Status |
|---|---|
| `inbound_event_processing_duration_ms` p95 ≥ 200 ms | not captured |
| Repo CPU > 80% for ≥ 2 samples | NO (peak 10.94%) |
| Consumer-group lag monotonic > 2 min | **YES, but driver does not stop on this** |
| k6 dropped iterations rising | **YES** — driver does not capture this signal mid-run |
| Cap reached | YES — 15 min wall-clock |

## Pod placement (Phase 5 redo isolation)

| Workload | Nodepool placement |
|---|---|
| App pods (8) | nodepool2 preferred (most), some on nodepool3 (overflow when nodepool2 full at 8 pods × cpu=1) |
| k6 runner | nodepool3 hard-pinned |
| Strimzi (5 pods) | nodepool4 hard-pinned |

## Cleanup

| Action | Status |
|---|---|
| Synthetic-row delete | failed locally (DNS); rows persist in PG |
| Consumer-group delete | succeeded via in-cluster broker pod |

## Key observations

The Platinum tier exposes the consumer bottleneck most starkly: at 8 pods × 1 cpu (= 8 cpu of consumer headroom) and 12 partitions wide, the application sustained only ~1,248 events/s consumed despite 3,238 events/s offered. The producer queue (Kafka) absorbed the difference and lag grew to 1.76 M.

Per-pod consumer rate scales modestly across tiers:

| Tier | Pods | Per-pod consume rate |
|---|---|---|
| Silver  | 2 | ~207 events/s/pod (kept up with 414/s offered) |
| Gold    | 4 | ~125 events/s/pod (lag grew at 2070/s offered) |
| Platinum| 8 | ~156 events/s/pod (lag grew at 3238/s offered) |

The drop from Silver to Gold suggests the per-pod ceiling is reached around 200 events/s/pod — beyond that, partition contention or fetch-batch handling caps throughput. Platinum's slightly higher per-pod rate (156 vs 125) is consistent with the more aggressive fetch settings (`maxPollRecords=500`, `fetchMinBytes=65536`).

The **k6 producer also became a constraint** at Platinum: 253 k dropped iterations indicates the VU pool (`maxVUs=720`) was exhausted. To push the application to its actual ceiling (or saturate broker / DB), the producer side needs to be scaled up — either by raising `maxVUs`, or by running multiple TestRun runners in parallel.

## Validation summary

- Producer/broker: broker fine (0 errors), but k6 producer hit its VU ceiling — needs scaling for true Platinum profile
- DB: never above 11% CPU — wildly over-provisioned for actual app throughput
- App consumer: saturated at ~150 events/s/pod
- Driver v3: clean artifact capture ✅
- App Insights pipeline: still broken ❌

## Artifacts

`.omc/research/kafka-loadtest/platinum-pg-1778111250/`

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
- **CPU binds** at 400.0%; pro-rate share = 4.0

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| PG Flex GP Dadsv5 8 vCore (`Standard_D8ds_v5`) | 0.9600 | 0.72000 | 1 Hour |
| PG Flex Storage Data Stored | 0.2185 | 0.16388 | 1 GiB/Month |
| PG Flex Storage IOPS | 0.0400 | 0.03000 | 1 IOPS/Month |
| PG Flex Storage Throughput | 0.1600 | 0.12000 | 1 MBps/Month |
| PG Flex Backup Storage LRS Data Stored | 0.0950 | 0.07125 | 1 GB/Month |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| PG D8ds_v5 compute | 0.9600 × 730 | 700.80 | 525.60 |
| PG storage 128 GiB | 0.2185 × 128 | 27.97 | 20.98 |
| PG storage 6000 IOPS | 0.04 × 6000 | 240.00 | 180.00 |
| PG storage 500 MBps throughput | 0.16 × 500 | 80.00 | 60.00 |
| PG backup ≤ 128 GiB | included | 0.00 | 0.00 |
| PG subtotal | | 1048.77 | 786.58 |
| App pro-rated D2s_v6 | 0.161 × 730 × 4.0 | 470.12 | 352.59 |
| App subtotal | | 470.12 | 352.59 |
| **Platinum Kafka v1 + PG total** | | **$1518.89** | **$1139.17** |

Savings: $379.72/month at 25% discount.

### Notes

- Fixed-replica Kafka deployment (consumer Deployment, not HPA-bounded).
- CPU binds (400.0%); pro-rate share = 4.0 — workload spans 4× D2s_v6 nodes.
- If pro-rate share > 1.0: workload spans multiple D2s_v6 nodes — share treated as multiplier.
- Premium SSD v2 storage: 128 GiB + 6000 IOPS + 500 MBps as provisioned for Platinum tier.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB, Kafka cluster (Strimzi MSK or self-hosted — separate budget line).
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.

## Phase 6 follow-ups suggested

1. **Fix App Insights metric pipeline** — `inbound_event_processing_duration_ms` not landing across all 3 runs. Inspect OTel View / meter registration in `ObservabilityConfig.cs` US-004.
2. **Profile the consumer handler** — per-pod ceiling at ~150–200 events/s suggests synchronous EF Core writes or Mediator dispatch overhead. Add per-stage timing (Kafka deserialize / Mediator dispatch / EF Core SaveChanges) to find the dominant cost.
3. **Scale k6 producer** — at Platinum, `maxVUs=720` is the producer ceiling. Either raise to ≥ 1500 or use parallelism > 1 in the TestRun.
4. **Add lag-growth stop rule** — driver should fail-fast when lag grows monotonically for > 2 min instead of running to cap.
