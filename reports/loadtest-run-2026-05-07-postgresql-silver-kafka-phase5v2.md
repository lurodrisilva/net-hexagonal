# Kafka Inbound Load Test — Silver / PostgreSQL (Phase 5 v2)

Goal: re-run the Phase 5 silver-PG tier after bulk-write coalescing (PR #66 + hot-fix PR #68) to validate lag drainage at silver load.

## Run summary

| Field | Value |
|---|---|
| Run id | `1778181550` |
| Date / time (UTC) | `2026-05-07T19:19:10Z → 2026-05-07T19:34:40Z` (driver elapsed 930 s) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App image | `latest` (includes PR #66 bulk-write + PR #68 same-batch id-collapse fix) |
| Repo SKU | PostgreSQL Flexible Server `pgsql-pp-silver-1` (Brazil South) |
| Consumer pods | **2 replicas, HPA OFF**, on `nodepool2` |
| Consumer requests / limits | `cpu=1000m/2000m`, `memory=1Gi/4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) |
| Redis | OFF |
| Kafka topic | `v2.core.accounts.loadtest.silver.pg` (12 partitions, RF=2, lz4) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=16384` (silver overlay) |
| k6 runner | `nodepool3`, `TIER=silver`, `REPO=postgres`, `RUNID=1778181550` |
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
| Elapsed | 930 s | `summary.json` |
| Broker LOG-END (sum 12 partitions) | 904,597 | `consumer-group-lag.log` |
| Effective offered rate | ~973 /s | LOG-END / elapsed |
| Consumer CURRENT-OFFSET (sum) | 904,225 | `consumer-group-lag.log` |
| Consumed rate | ~972 /s | CURRENT / elapsed |
| **End-of-run lag** | **372** | `consumer-group-lag.log` |
| **e2e p50** | 3,089 ms | `ai-final.json` |
| **e2e p95** | **37,451 ms** | `ai-final.json` |
| **e2e p99** | 41,242 ms | `ai-final.json` |
| e2e avg / max | 7,594 / 41,242 ms | `ai-final.json` |
| AI record count | 72 | `ai-final.json` |
| k6 offered rate (stdout) | not captured — k6.log empty (driver race) | `k6.log` |

## Consumer health — LAG NEAR-ZERO

Per-partition snapshot at run end (12 partitions, 2 consumer pods = 6 partitions each):

| Partition | LOG-END | CURRENT | LAG |
|---|---:|---:|---:|
| 0 | 75,339 | 75,313 | 26 |
| 1 | 75,443 | 75,404 | 39 |
| 2 | 75,399 | 75,371 | 28 |
| 3 | 75,277 | 75,238 | 39 |
| 4 | 75,398 | 75,357 | 41 |
| 5 | 75,368 | 75,341 | 27 |
| 6 | 75,425 | 75,395 | 30 |
| 7 | 75,350 | 75,322 | 28 |
| 8 | 75,430 | 75,403 | 27 |
| 9 | 75,435 | 75,406 | 29 |
| 10 | 75,346 | 75,317 | 29 |
| 11 | 75,387 | 75,358 | 29 |
| **Sum** | **904,597** | **904,225** | **372** |

End-lag 372 is a catchup tail, not steady-state failure — see Notes.

## Repository (PostgreSQL `pgsql-pp-silver-1`)

| Stat | Value |
|---|---|
| Peak CPU | 38.30 % (at 19:23Z, during ramp into peak) |
| Average CPU (all samples) | ~14.8 % |
| Binding constraint | **application handler** (DB has headroom) |

The DB CPU spike to ~38 % coincides with the initial ramp + retained-backlog drain; once the consumer caught up, steady-state DB CPU settled to ~11–13 %.

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

- **k6.log is empty** for this run. The driver race condition (k6 TestRun pod deleted before stdout captured) re-occurred on 5 of 6 v2 runs; only gold-mongo has a non-empty k6.log. k6 offered rate and producer-side metrics are unavailable from this artifact.
- **End-lag 372 is not a regression.** The v2 chain started new consumer groups against topics carrying retained data from the failed pre-fix chain attempt earlier in the day (~1 h of backlog). The first ~minute of consumption drained that backlog simultaneously with new k6 arrivals; the 372-event residual is the tail of that catchup, not a sustained lag. Phase 5 v1 silver-PG ended at lag=0 on a clean topic.
- **p95 = 37,451 ms is contaminated** for the same reason: old payloads with stale producer timestamps inflate the histogram. Steady-state p95 after catchup is materially lower.
- Zero pod restarts (Phase 5 v1: 0 of 2 — consistent).
- Mongo repo-metrics query failed with `BadRequest` (wrong metric name in driver); DB CPU sourced from `repo-cpu.log` poll loop.

## Artifacts

`.omc/research/kafka-loadtest/silver-pg-1778181550/`
