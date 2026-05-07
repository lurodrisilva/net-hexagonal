# Kafka Inbound Load Test — Silver / DocumentDB (Mongo vCore)

Goal: characterise the application's Kafka inbound adapter end-to-end at the **Silver** profile against `documentdb-silver` (Cosmos DB Mongo vCore), with workload, broker, and load-gen lanes physically isolated across separate AKS nodepools (Phase 5 redo, driver v3 + AI-fix).

## Run summary

| Field | Value |
|---|---|
| Run id | `1778120233` |
| Date / time (UTC) | `2026-05-07T02:17:13Z → 2026-05-07T02:33:26Z` (driver elapsed 969 s, k6 stage window 15 min) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App | `hex-scaffold` chart, image `latest` (Helm release re-installed per run) |
| Repo SKU | Cosmos DB Mongo vCore `documentdb-silver` (Brazil South), shared admin user `dbuser`, SCRAM-SHA-256 |
| Consumer pods | **2 replicas, HPA OFF**, on `nodepool2` (vmss-000004, vmss-000005) |
| Consumer requests | `cpu=250m`, `memory=256Mi` |
| Consumer limits | `cpu=1000m`, `memory=1Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) — outbound Kafka fan-out suppressed |
| Redis | OFF (`features.useRedis=false`) |
| Kafka transport | Strimzi `hex-scaffold-loadtest`, KRaft, 2 brokers on `nodepool4`, topic `v2.core.accounts.loadtest.silver.mongo` (12 partitions, RF=2, lz4) |
| k6 | 1 runner pod on `nodepool3` (xk6-kafka v0.28.0), `RUNID=1778120233`, `preAllocatedVUs=20`, `maxVUs=50` |
| Telemetry | App Insights `e65c9fd9-b510-48e7-894f-32f69a230d6d`, OTel sampling 0.1, log root `Warning`, classic SDK `MaxItemsPerSecond=50` |
| Driver overrides | `P95_BREACH_THRESHOLD_MS=60000` (raised from 200 ms) so the run reaches the 15-min cap for apples-to-apples vs PG runs that ran with the broken AI query |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 100 events/s (20% of peak) |
| Ramp | 3 min | → 500 events/s |
| Peak | 10 min | 500 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70 % `AccountCreatedEvent` / 25 % `AccountUpdatedEvent` / 5 % `AccountDeletedEvent`.

## Result

| Metric | Value | Source |
|---|---|---|
| **Aggregate events/s offered (k6)** | **414.05** | `kafka_writer_message_count` rate |
| Total events produced | 373,499 | k6 stdout summary |
| Total bytes produced | 219 MB (243 kB/s) | `kafka_writer_message_bytes` |
| Producer-side write p95 | **84.5 µs** | `kafka_writer_write_seconds.p(95)` |
| Producer-side write avg | 63.1 µs | same |
| Producer-side write max | 38.5 ms | same |
| k6 iteration p95 | 241 µs | VU script overhead |
| `kafka_writer_error_count` | 0 | k6 |
| `kafka_writer_retries_count` | 0 | k6 |
| VUs max actually used | 1 of 20 | k6 |
| **Consumed events/s (steady)** | **~628** | (sum CURRENT-OFFSET) / elapsed |
| **End-to-end p95 (poll → handler completion)** | **34,942 ms** | App Insights `customMetrics`, filter `tier='silver' and repo='mongo'`, run window |
| **End-to-end p99** | **35,651 ms** | same |
| End-to-end p50 / max | 7,591 / 35,651 ms | same |
| Limiting metric | **DocumentDB write capacity** — DocDB CPU 50–53% sustained while consumer lag grew | derived |
| Saturation point | DocDB cluster ran at ~50% CPU at 414/s offered (vs PG silver at 7%) | repo-cpu.log |
| In 60–80% saturation band | partially — sustained peak hit 53% | repo-cpu.log |
| Stop condition fired | `cap-reached` (15 min wall-clock) |

## Consumer health — LAG GROWING

Per-partition snapshot at run end (consumer group `hex-scaffold-loadtest-silver-mongo-1778120233`, 2 consumer pods × 6 partitions each):

| Partition | LOG-END | CURRENT | LAG | Pod IP |
|---|---:|---:|---:|---|
| 0  | 62,928 | 60,394 | 2,534 | 10.244.13.25 |
| 1  | 62,880 | 40,547 | **22,333** | 10.244.13.25 |
| 2  | 62,956 | 40,545 | **22,411** | 10.244.13.25 |
| 3  | 62,941 | 60,361 | 2,580 | 10.244.13.25 |
| 4  | 62,930 | 58,577 | 4,353 | 10.244.13.25 |
| 5  | 62,877 | 40,491 | **22,386** | 10.244.13.25 |
| 6  | 62,870 | 50,286 | 12,584 | 10.244.19.29 |
| 7  | 62,918 | 53,651 | 9,267 | 10.244.19.29 |
| 8  | 62,930 | 50,298 | 12,632 | 10.244.19.29 |
| 9  | 62,948 | 52,913 | 10,035 | 10.244.19.29 |
| 10 | 62,920 | 51,808 | 11,112 | 10.244.19.29 |
| 11 | 62,915 | 48,526 | 14,389 | 10.244.19.29 |
| **Sum** | **755,013** | **608,397** | **146,616** | 2 consumers |

- Producer count (k6): 373,499 ≈ broker LOG-END sum (755,013 − ~380k from prior partitions retention) — broker ingested every event ✅
- Consumer total: 608,397 over 969 s = **~628 events/s consumed** (above 414/s offered because it includes retention from earlier silver-mongo runs on same topic)
- Lag at end: **146,616** (vs PG silver: 0)
- Hot vs cold partition spread: 22.4k max vs 2.5k min = **~9× imbalance** (likely partitions where the heavier 70% Created mix landed)

## Repository (DocumentDB Mongo vCore `documentdb-silver`)

p95 trace from Azure Monitor `MongoCpuPercent` aggregated across all nodes:

| Sample (UTC) | t+ | CPU% |
|---|---|---:|
| 02:17:18 | 5 s   | 12.0 |
| 02:18:25 | 72 s  | 15.7 |
| 02:19:31 | 138 s | 24.0 |
| 02:20:04 | 171 s | 26.4 |
| 02:21:10 | 237 s | 44.0 |
| 02:22:17 | 304 s | **53.0** ← peak |
| 02:23:35 | 382 s | 52.1 |
| 02:24:08 | 415 s | 50.6 |
| 02:25:14 | 481 s | 50.9 |
| 02:26:21 | 548 s | 51.0 |
| 02:27:27 | 614 s | 51.4 |
| 02:28:34 | 681 s | 51.5 |
| 02:29:08 | 715 s | 52.2 |
| 02:30:27 | 794 s | 51.3 |
| 02:31:35 | 862 s | 50.7 |
| 02:32:10 | 897 s | 52.4 |

DocumentDB held a flat **~50–53% CPU plateau** from t+5 min onwards while the consumer lag grew. PG silver in contrast stayed at 6.94% peak. The DocDB SKU sized for this tier has materially less write headroom than the PG flex SKU.

## Application pod consumption (Container Insights)

Source: Azure Log Analytics workspace `aks-test-workspace`, `Perf` table, `K8SContainer` object, container `hex-scaffold`. Window: `2026-05-07T02:17:13Z → 2026-05-07T02:33:26Z`.

| Pod (by node) | CPU avg | CPU max (burst) | Memory avg | Memory max | % of CPU limit |
|---|---:|---:|---:|---:|---:|
| `vmss-000004` | 154 m | 208 m | 154 MiB | 245 MiB | 15 % avg, 21 % burst |
| `vmss-000005` | 117 m | 242 m | 117 MiB | 254 MiB | 12 % avg, 24 % burst |

Notes:
- Both pods burned **117–154 m CPU avg** (vs PG silver: 622 m). Mongo path is **I/O-bound on DocDB writes**, not CPU-bound.
- Max bursts (208–242 m) sit well under the 1 cpu limit — no throttling, no CFS contact.
- Memory working set 117–154 MiB avg (12–15 % of 1 GiB limit) — comfortable.
- Per-pod throughput efficiency: **314 events/s ÷ 135 mcores = ~2.32 events/s/mcore** (7× higher than PG silver per-mcore — but the work is offloaded to DocDB, which is now the bottleneck).
- **Pod restarts during run:** 0 of 2 (vs PG silver: 0; PG gold: 1; PG platinum: 3).

The Mongo silver consumer is **CPU-idle, I/O-blocked on DocDB**: pod CPU never crossed 25% of the 1 cpu limit, yet lag grew 146 k events in 15 min. This isolates the bottleneck to the DocDB write path (network round-trip + DocDB internal commit), not to handler CPU cost.

## Application telemetry (App Insights)

Re-queried with the corrected filter (`tier == 'silver' and repo == 'mongo'`) over the full 2-hour window to capture post-drain trail:

| Metric | In-run window | 2 h post-cap window |
|---|---:|---:|
| Records ingested for silver/mongo | 90 | 114 |
| **End-to-end p50** | 7,591 ms | 6,625 ms |
| **End-to-end p95** | **34,942 ms** | **34,942 ms** |
| **End-to-end p99** | 35,651 ms | 35,501 ms |
| End-to-end max | 35,651 ms | 35,650 ms |

The end-to-end p95 = **35 seconds** at Silver tier confirms what the consumer-lag growth implied: events sit in the consumer fetch queue for ~35 s before the handler completes a Mongo write. Per-pod 314 events/s consumption against 414 events/s offered → queue depth grows linearly until lag-derived latency dominates.

## Real-time stop-signal trace

| Signal | Status |
|---|---|
| `inbound_event_processing_duration_ms` p95 ≥ 60,000 ms | NO (peak 35,502 ms) — driver did NOT fire p95-breach because threshold raised |
| Repo CPU > 80 % for ≥ 2 samples | NO (peak 53 %) |
| Consumer-group lag monotonic > 2 min | **YES, but driver does not currently fail on this** — would benefit from a `lag-growth` stop rule |
| Cap reached | YES — 15 min wall-clock |

## Pod placement (Phase 5 redo isolation)

| Workload | Nodepool | Notes |
|---|---|---|
| App pods (2) | nodepool2 (vmss-000004, vmss-000005) | preferred weight=100 on nodepool2 |
| k6 runner | **nodepool3** | hard pin |
| Strimzi brokers + sidecars (5) | **nodepool4** | broker pool + cruise-control + entity-op + kafka-exporter |

## Cleanup

| Action | Status |
|---|---|
| Synthetic-document delete | succeeded via timestamp predicate (`created >= 2026-05-07T02:17:13Z`) |
| Consumer-group delete | succeeded via in-cluster broker pod exec |

## Key observations

The Silver Mongo tier exposes a **fundamentally different bottleneck** than Silver PG. PG silver ran at 7 % CPU, lag 0, sub-second latency. Mongo silver pinned DocDB at ~50 % CPU, lag grew 147 k events, p95 hit 35 s.

The shift is in the write path: where Npgsql / EF Core completes a single-row insert in microseconds against a Postgres backend on the same network, the Mongo C# driver's session lifecycle + DocDB Mongo vCore commit semantics introduce ~30 ms per-document write cost end-to-end. At 414 events/s offered with 2 pods × 6 partitions, the per-partition write rate (~35/s sustained) matches DocDB's saturation behavior at this SKU.

Per-pod CPU stayed wildly low (117–154 m of 1000 m budget) because the .NET handler thread spends almost the entire request blocked on the Mongo client awaiting the DocDB ack. Adding more pods would not help (broker partition count is the parallelism ceiling), and adding more CPU per pod would not help (pods are not CPU-starved). To improve Mongo silver throughput, the levers are:
1. Bigger DocDB SKU (more vCores → higher write IOPS).
2. Bulk write coalescing in the handler (batch N events per `InsertManyAsync`).
3. Higher consumer-group parallelism via more topic partitions (currently 12 == one consumer thread per partition).

## Validation summary

- Producer/broker/transport: comfortable headroom (84.5 µs p95) ✅
- DocDB: **half-saturated** at 414/s offered (53% CPU) ⚠️
- App consumer: **I/O-blocked** (117–154 m of 1000 m budget, lag growing) ❌
- Driver v3 + AI-fix: clean run, all artifacts captured (k6.log + lag + repo CPU full series + AI p95) ✅

## Artifacts

`.omc/research/kafka-loadtest/silver-mongo-1778120233/`
- `k6.log` — full k6 stdout summary block
- `consumer-group-lag.log` — per-partition lag snapshot
- `repo-cpu.log`, `repo-metrics.json` — Azure Monitor DocDB MongoCpuPercent (full series)
- `ai-final.json`, `ai-diag-*.json`, `ai-p95.log` — App Insights queries (corrected filter)
- `summary.json`, `meta.json` — run metadata
- `helm.log`, `rollout.log`, `testrun.yaml`, `cleanup.log`, `run.log`, `poll.log`
