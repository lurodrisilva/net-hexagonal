# Kafka Inbound Load Test — Gold / DocumentDB (Phase 5 v2)

Goal: re-run the Phase 5 gold-Mongo tier after bulk-write coalescing (PR #66 + PR #68) and 12→24 partition expansion (PR #65) to close the 1.48 M end-lag from v1.

## Run summary

| Field | Value |
|---|---|
| Run id | `1778185316` |
| Date / time (UTC) | `2026-05-07T20:21:56Z → 2026-05-07T20:37:52Z` (driver elapsed 956 s) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App image | `latest` (includes PR #66 bulk-write + PR #68 same-batch id-collapse fix) |
| Repo SKU | Cosmos DB Mongo vCore `documentdb-gold` (Brazil South) |
| Consumer pods | **4 replicas, HPA OFF**, on `nodepool2` |
| Consumer requests / limits | `cpu=1000m/2000m`, `memory=1Gi/4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) |
| Redis | OFF |
| Kafka topic | `v2.core.accounts.loadtest.gold.mongo` (**24 partitions**, RF=2, lz4 — expanded via PR #65) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=32768` (gold overlay) |
| k6 runner | `nodepool3`, `TIER=gold`, `REPO=mongo`, `RUNID=1778185316` |
| Driver | v3 + AI-fix + `P95_BREACH_THRESHOLD_MS=60000` |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 250 events/s |
| Ramp | 3 min | → 2,500 events/s |
| Peak | 10 min | 2,500 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70% Create / 25% Update / 5% Delete.

## Result

| Metric | Value | Source |
|---|---|---|
| Stop reason | `cap-reached` (15-min wall clock) | `summary.json` |
| Elapsed | 956 s | `summary.json` |
| Broker LOG-END (sum 24 partitions) | 3,997,757 | `consumer-group-lag.log` |
| Effective offered rate | ~4,182 /s | LOG-END / elapsed |
| Consumer CURRENT-OFFSET (sum) | 3,997,757 | `consumer-group-lag.log` |
| Consumed rate | ~4,182 /s (matched offered) | CURRENT / elapsed |
| **End-of-run lag** | **0** | `consumer-group-lag.log` |
| **e2e p50** | 2,206 ms | `ai-final.json` |
| **e2e p95** | **7,803 ms** | `ai-final.json` |
| **e2e p99** | 10,860 ms | `ai-final.json` |
| e2e avg / max | 2,964 / 12,731 ms | `ai-final.json` |
| AI record count | 180 | `ai-final.json` |
| **k6 offered rate (stdout)** | **~2,079 /s** (1,874,999 iterations / 956 s) | `k6.log` (26 lines — only non-empty k6.log in the batch) |
| `kafka_writer_error_count` | 0 | `k6.log` |
| `kafka_writer_write_seconds` p95 | 71.18 µs | `k6.log` |

## Consumer health — ZERO LAG

All 24 partitions at lag = 0 at run end:

| Pod group | Partitions | LOG-END range | LAG |
|---|---|---|---|
| 10.244.7.124 (pod A) | 0–5 | 246,211–246,675 | 0 each |
| 10.244.11.37 (pod B) | 6–11 | 246,330–246,479 | 0 each |
| 10.244.12.28 (pod C) | 12–17 | 86,672–86,817 | 0 each |
| 10.244.14.32 (pod D) | 18–23 | 86,641–86,765 | 0 each |
| **Sum** | **24** | **3,997,757** | **0** |

Partitions 0–11 carried ~246 k events each (older topic offsets + k6 stream); partitions 12–23 carried ~87 k events each (newer partitions added by PR #65). Both pod pairs drained fully before the 15-min cap.

## k6 producer summary (gold-mongo only)

This is the **only run in the Phase 5 v2 batch with a non-empty k6.log** (26 lines captured). The driver race condition that deletes the TestRun pod before stdout is read did not fire on this run.

| k6 metric | Value |
|---|---|
| Total iterations | 1,874,999 |
| Iteration rate | ~2,079 /s |
| `kafka_writer_error_count` | 0 |
| `kafka_writer_write_seconds` p95 | 71.18 µs |
| `kafka_writer_batch_bytes` total | 1.1 GB |
| `kafka_writer_retries_count` | 0 |
| VUs max | 100 |

Zero producer errors and zero retries — the broker absorbed every message cleanly.

## Repository (DocumentDB `documentdb-gold`)

| Stat | Value |
|---|---|
| Peak CPU | 11.00 % (at 20:33Z) |
| Average CPU (all samples) | ~9.0 % |
| Binding constraint | **application handler** (DB has ample headroom) |

Gold DocDB CPU is well below any saturation threshold. The Mongo handler is I/O-blocked (async-await on `BulkWriteAsync`), not CPU-active; 4 pods at gold have enough concurrency to keep the DB write path busy without ever stressing the database.

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

- **k6.log has 26 lines** — the only non-empty k6.log in the batch. Offered rate from k6 (~2,079 /s) is lower than the broker-side effective offered rate (~4,182 /s) because the topic carried retained events from prior gold-mongo runs; the consumer consumed both the live k6 stream and the backlog simultaneously.
- **-100% lag improvement vs v1** (1,480,000 → 0). Gold-Mongo drains fully where gold-PG still carried 40,688 residual lag. The Mongo I/O-blocked handler benefits more from bulk batching on the async-await path than the PG CPU-active path does.
- DocumentDB `repo-metrics.json` captured a `BadRequest` error (wrong metric name); DB CPU sourced from `repo-cpu.log`.

## Artifacts

`.omc/research/kafka-loadtest/gold-mongo-1778185316/`

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | Run summary: 4 consumer pods | 4 |
| CPU reserved at peak | 4 × cpu=1000m | 4000m |
| Memory reserved at peak | 4 × memory=1Gi | 4096 Mi (4 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 4000m / 2000m = 200.0%
- Memory: 4096 Mi / 8192 Mi = 50.0%
- **CPU binds** at 200.0%; pro-rate share = 2.0 (workload spans multiple D2s_v6 nodes — share = node multiplier)

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
| App pro-rated D2s_v6 | 0.161 × 730 × 2.0 | 235.06 | 176.30 |
| App subtotal | | 235.06 | 176.30 |
| **Gold Kafka v2 + DocDB total** | | **~$381.06** | **~$285.80** |

Savings: ~$95.26/month at 25% discount.

### Notes

- Fixed-replica Kafka deployment (not HPA-bounded).
- CPU binds (200.0%); pro-rate uses binding dimension.
- If pro-rate share > 1.0: workload spans multiple D2s_v6 nodes — share = node multiplier.
- DocDB M30 unit price estimated — Cosmos DB for MongoDB vCore tiers are not a single per-tier line in the public Retail Prices API; values consistent with public Cosmos DB MongoDB vCore pricing tables.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB, Kafka cluster (separate budget line).
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
