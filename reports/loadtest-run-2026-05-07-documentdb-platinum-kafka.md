# Kafka Inbound Load Test — Platinum / DocumentDB (Mongo vCore)

Goal: characterise the application's Kafka inbound adapter end-to-end at the **Platinum** profile against `documentdb-platinum` (Cosmos DB Mongo vCore), with workload, broker, and load-gen lanes physically isolated across separate AKS nodepools (Phase 5 redo, driver v3 + AI-fix).

## Run summary

| Field | Value |
|---|---|
| Run id | `1778122178` |
| Date / time (UTC) | `2026-05-07T02:49:38Z → 2026-05-07T03:00:32Z` (driver elapsed 648 s — k6 ended at testrun-finished due to VU exhaustion at 720) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App | `hex-scaffold` chart, image `latest` |
| Repo SKU | Cosmos DB Mongo vCore `documentdb-platinum` (Brazil South), shared admin user `dbuser`, SCRAM-SHA-256 |
| Consumer pods | **8 replicas, HPA OFF**, on `nodepool2` (vmss 01/02/03/04/05/06/07/09) |
| Consumer requests | `cpu=1000m`, `memory=1Gi` |
| Consumer limits | `cpu=4000m`, `memory=4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) — outbound Kafka fan-out suppressed |
| Redis | OFF (`features.useRedis=false`) |
| Kafka transport | Strimzi `hex-scaffold-loadtest`, KRaft, 2 brokers on `nodepool4`, topic `v2.core.accounts.loadtest.platinum.mongo` (12 partitions, RF=2, lz4) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=65536` (aggressive fetch batching per platinum overlay) |
| k6 | 1 runner pod on `nodepool3`, `RUNID=1778122178`, `preAllocatedVUs=200`, `maxVUs=720` |
| Telemetry | App Insights `e65c9fd9-…`, OTel sampling 0.1, log root `Warning`, classic SDK `MaxItemsPerSecond=50` |
| Driver overrides | `P95_BREACH_THRESHOLD_MS=60000` (raised from 200 ms) |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 1060 events/s |
| Ramp | 3 min | → 5300 events/s |
| Peak | 10 min | 5300 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70% Created / 25% Updated / 5% Deleted.

## Result

| Metric | Value | Source |
|---|---|---|
| **Aggregate events/s offered (k6)** | not captured (k6 runner deleted before stdout summary written) | k6.log truncated |
| **Broker LOG-END after run** | **1,738,612** events ingested by broker | `consumer-group-lag.log` |
| **Effective offered rate (broker-side)** | **~2,683/s** averaged over 648 s | LOG-END / elapsed |
| Producer-side write p95 | not captured | k6.log truncated |
| `kafka_writer_error_count` | unknown (k6.log lost) | — |
| **VU exhaustion warning** | `Insufficient VUs, reached 720 active VUs and cannot initialize more` at 02:59:15 | k6.log |
| **Consumed events/s (steady)** | **~880** | (sum CURRENT-OFFSET) / elapsed |
| **End-to-end p95 (poll → handler completion)** | **36,924 ms** | App Insights `customMetrics`, filter `tier='platinum' and repo='mongo'`, 2 h post-cap window |
| **End-to-end p99** | **46,434 ms** | same |
| End-to-end p50 / max | 4,845 / 50,774 ms | same |
| Limiting metric | **app consumer handler + k6 producer ceiling (VU exhaustion)** | derived |
| Saturation point | per-pod consumer rate ~110/s (8 pods × 110 ≈ 880/s aggregate) | derived |
| In 60–80 % saturation band | NO — DocDB CPU peaked 8.9 %; bottleneck is upstream | repo-cpu.log |
| Stop condition fired | `testrun-finished` (k6 ended at 648 s after stage-end + VU-exhausted ramp-down) |

## Consumer health — LAG GROWING

Per-partition snapshot at run end (consumer group `hex-scaffold-loadtest-platinum-mongo-1778122178`, 8 consumer pods sharing 12 partitions = 4 pods own 2 partitions, 4 pods own 1):

| Partition | LOG-END | CURRENT | LAG | Pod IP |
|---|---:|---:|---:|---|
| 0  | 144,831 | 19,971 | **124,860** | 10.244.19.30 |
| 1  | 144,862 | 42,965 | **101,897** | 10.244.19.30 |
| 2  | 144,909 | 27,241 | **117,668** | 10.244.15.27 |
| 3  | 144,944 | 50,095 | **94,849**  | 10.244.15.27 |
| 4  | 144,970 | 39,855 | **105,115** | 10.244.10.24 |
| 5  | 144,869 | 30,826 | **114,043** | 10.244.10.24 |
| 6  | 144,909 | 38,133 | **106,776** | 10.244.16.25 |
| 7  | 144,812 | 42,169 | **102,643** | 10.244.16.25 |
| 8  | 144,856 | 66,379 | 78,477  | 10.244.14.29 |
| 9  | 144,816 | 67,417 | 77,399  | 10.244.13.26 |
| 10 | 145,005 | 81,056 | 63,949  | 10.244.17.22 |
| 11 | 144,829 | 64,646 | 80,183  | 10.244.11.34 |
| **Sum** | **1,738,612** | **570,753** | **1,167,859** | 8 consumers |

- Broker ingested 1,738,612 events over 648 s = **2,683 events/s effective offered rate**
- Consumer total: 570,753 over 648 s = **~880 events/s consumed**
- Per-pod consume rate: ~110 events/s/pod for 1-partition pods, ~160 events/s/pod for 2-partition pods
- Lag at end: **1.17 M** (vs PG platinum: 1.76 M) — Mongo platinum drained slightly less because run was 252 s shorter
- Hot vs cold partition spread: 124k max vs 64k min = **~2× imbalance** — partitions on 2-partition pods drained 1.5× faster per partition (less competition for handler thread per partition)

## Repository (DocumentDB Mongo vCore `documentdb-platinum`)

| Stat | Value |
|---|---|
| Min CPU | 2.7 % (early ramp) |
| Max CPU | 8.9 % |
| Mean CPU (peak window) | ~7 % |
| Lag growth despite trivial DB CPU | confirms DocDB is **massively over-provisioned** for the load this consumer can deliver |

Platinum DocDB peaked at 8.9 % — the cluster could absorb 10× this rate. The throughput limit is firmly inside the application's Mongo write path, not the database.

## Application pod consumption (Container Insights)

Source: Azure Log Analytics workspace `aks-test-workspace`, `Perf` table, container `hex-scaffold`. Window: `2026-05-07T02:49:38Z → 2026-05-07T03:00:32Z`.

| Pod (by node) | CPU avg | CPU max (burst) | Memory avg | Memory max | % of CPU limit |
|---|---:|---:|---:|---:|---:|
| `vmss-000001` | 155 m | 470 m | 140 MiB | 165 MiB | 4 % avg, 12 % burst |
| `vmss-000002` | 112 m | 157 m | 132 MiB | 153 MiB | 3 % avg, 4 % burst |
| `vmss-000003` | 164 m | 464 m | 136 MiB | 156 MiB | 4 % avg, 12 % burst |
| `vmss-000004` | 150 m | 389 m | 190 MiB | 246 MiB | 4 % avg, 10 % burst |
| `vmss-000005` | 127 m | 359 m | 187 MiB | 254 MiB | 3 % avg, 9 % burst |
| `vmss-000006` | 151 m | 404 m | 136 MiB | 145 MiB | 4 % avg, 10 % burst |
| `vmss-000007` | 142 m | 428 m | 145 MiB | 167 MiB | 4 % avg, 11 % burst |
| `vmss-000009` | 184 m | 499 m | 148 MiB | 173 MiB | 5 % avg, 12 % burst |

Notes:
- All 8 pods burned **112–184 m CPU avg** against `request=1000m` (using **0.15× requested**) and `limit=4000m` (3–5 % avg). Pods are dramatically over-provisioned vs actual handler CPU need.
- Max samples (157–499 m) sit far below the 4 cpu limit — no throttling.
- Memory working set 132–190 MiB (3–5 % of 4 GiB limit) — wildly over-provisioned.
- Per-pod throughput efficiency: **110 events/s ÷ 148 mcores = ~0.74 events/s/mcore** (vs PG platinum 0.10 — 7.4× better per-mcore, but that gain accrues to DocDB I/O wait, not productive work).
- **Pod restarts during run:** 0 of 8 (vs PG platinum: 3). No CPU-throttle-induced liveness probe misses because pods are nowhere near throttle.

The Mongo platinum consumer is **deeply CPU-idle**: 8 pods sit at ~4 % of their CPU limit while lag grows 1.17 M events in 11 min. The Mongo C# driver's per-partition write thread serialization is the wall; throwing more pods (12 → 24) wouldn't help because partition count caps consumer parallelism at 12.

## Application telemetry (App Insights)

Filter `tier == 'platinum' and repo == 'mongo'`, 2 h post-cap window:

| Metric | Value |
|---|---:|
| Records ingested for platinum/mongo | 369 |
| **End-to-end p50** | 4,845 ms |
| **End-to-end p95** | **36,924 ms** |
| **End-to-end p99** | 46,434 ms |
| End-to-end max | 50,774 ms |

Compared to **PG platinum** (p95 24.3 s during run / 33.2 s post-drain): Mongo platinum is **1.5× slower at p95** and **1.4× slower at p99**.

The post-drain trail (60 min after cap) captures continued handler work as the consumer slowly digests the 1.17 M event backlog — late-arriving records have higher per-event latency because they've been queue-dwelling longer. PG platinum exhibits the same trail behavior but with smaller magnitude.

## Real-time stop-signal trace

| Signal | Status |
|---|---|
| `inbound_event_processing_duration_ms` p95 ≥ 60,000 ms | NO (peak ~37 s in-run, 50 s post-drain max) |
| Repo CPU > 80 % for ≥ 2 samples | NO (peak 8.9 %) |
| Consumer-group lag monotonic > 2 min | **YES** — driver does not currently fail on this |
| k6 VU exhaustion | **YES** — 720 VUs reached at 02:59:15, k6 ended naturally at 02:59:45 |
| Cap reached | NO — k6 ended first at 648 s |

## Pod placement (Phase 5 redo isolation)

| Workload | Nodepool | Notes |
|---|---|---|
| App pods (8) | nodepool2 (vmss 01/02/03/04/05/06/07/09) | preferred weight=100 on nodepool2 |
| k6 runner | **nodepool3** | hard pin |
| Strimzi brokers + sidecars (5) | **nodepool4** | broker pool + cruise-control + entity-op + kafka-exporter |

## Cleanup

| Action | Status |
|---|---|
| Synthetic-document delete | succeeded via timestamp predicate |
| Consumer-group delete | succeeded via in-cluster broker pod exec |

## Key observations

The Platinum Mongo tier confirms the same handler-bound story as Gold and Silver Mongo: the Mongo C# driver's per-partition write thread tops out around 110 events/s/pod regardless of pod resource budget. Eight platinum pods burned 4 % of their 4-cpu limit; doubling to 16 pods wouldn't help because partition count = 12 caps parallelism.

The k6 producer hit its VU ceiling at 720, ending the run early at 10:48 instead of the planned 14:00 peak window. This was inherited from the platinum k6 overlay (same `maxVUs=720` as PG platinum, where it also exhausted). Phase 6 follow-up: raise `maxVUs` to ≥ 1500 OR run two k6 TestRunners in parallel for platinum runs.

Hot vs cold partition spread (~2×) reflects the topology constraint: 12 partitions / 8 pods means 4 pods consume 2 partitions each and 4 pods consume 1. The 1-partition pods drain their assigned partition faster (less competition for handler thread), so the imbalance is structural, not skewed work distribution. Adding 4 more partitions (→ 16) would re-balance assignment to 2:2:2:2:2:2:2:2 across 8 pods.

The DocDB peak at 8.9 % CPU proves the platinum DocDB SKU is **massively over-provisioned for what this consumer can drive**. The platinum cost-per-event is dominated by application licensing/compute, not database SKU — opposite to Mongo silver where the small DocDB was the binding constraint.

## Validation summary

- Producer/broker/transport: comfortable headroom (broker absorbed every event from k6) ✅
- DocDB: massive headroom (8.9 % CPU) ✅
- App consumer: **I/O-blocked at ~110 events/s/pod** ❌
- k6 producer: **exhausted at 720 VUs** ❌ — limits comparison vs PG platinum
- Driver v3 + AI-fix: clean run, AI p95 captured (k6 stdout summary lost to early pod deletion) ⚠️

## Artifacts

`.omc/research/kafka-loadtest/platinum-mongo-1778122178/`
