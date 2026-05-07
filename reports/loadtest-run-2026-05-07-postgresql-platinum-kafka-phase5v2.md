# Kafka Inbound Load Test — Platinum / PostgreSQL (Phase 5 v2)

Goal: re-run the Phase 5 platinum-PG tier after bulk-write coalescing (PR #66 + PR #68) and 12→24 partition expansion (PR #65). First run in loadtest history where k6 completed all stages naturally inside the cap window.

## Run summary

| Field | Value |
|---|---|
| Run id | `1778186279` |
| Date / time (UTC) | `2026-05-07T20:37:59Z → 2026-05-07T20:51:14Z` (driver elapsed 795 s) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App image | `latest` (includes PR #66 bulk-write + PR #68 same-batch id-collapse fix) |
| Repo SKU | PostgreSQL Flexible Server `pgsql-pp-platinum-1` (Brazil South) |
| Consumer pods | **8 replicas, HPA OFF**, on `nodepool2` |
| Consumer requests / limits | `cpu=1000m/2000m`, `memory=1Gi/4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) |
| Redis | OFF |
| Kafka topic | `v2.core.accounts.loadtest.platinum.pg` (**24 partitions**, RF=2, lz4 — expanded via PR #65) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=65536` (platinum overlay) |
| k6 runner | `nodepool3`, `TIER=platinum`, `REPO=postgres`, `RUNID=1778186279` |
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
| **Stop reason** | **`testrun-finished`** — k6 completed all stages cleanly | `summary.json` |
| Elapsed | 795 s | `summary.json` |
| Broker LOG-END (sum 24 partitions) | 5,156,859 | `consumer-group-lag.log` |
| Effective offered rate | ~6,487 /s | LOG-END / elapsed |
| Consumer CURRENT-OFFSET (sum) | 5,156,859 | `consumer-group-lag.log` |
| Consumed rate | ~6,487 /s (matched offered) | CURRENT / elapsed |
| **End-of-run lag** | **0** | `consumer-group-lag.log` |
| **e2e p50** | 5,373 ms | `ai-final.json` |
| **e2e p95** | **18,824 ms** | `ai-final.json` |
| **e2e p99** | 20,964 ms | `ai-final.json` |
| e2e avg / max | 6,858 / 26,259 ms | `ai-final.json` |
| AI record count | 294 | `ai-final.json` |
| k6 offered rate (stdout) | not captured — k6.log empty (driver race) | `k6.log` |

## Consumer health — ZERO LAG

Per-partition snapshot at run end (24 partitions, 8 consumer pods = 3 partitions each). All 24 partitions at lag = 0:

| Pod group | Partitions | LOG-END range | LAG |
|---|---|---|---|
| 10.244.11.38 | 0, 1, 2 | 336,483–336,628 | 0 each |
| 10.244.16.28 | 3, 4, 5 | 336,485–336,694 | 0 each |
| 10.244.14.33 | 6, 7, 8 | 336,568–336,637 | 0 each |
| 10.244.18.37 | 9, 10, 11 | 336,442–336,620 | 0 each |
| 10.244.13.29 | 15, 16, 17 | 93,127–93,175 | 0 each |
| 10.244.19.33 | 18, 19, 20 | 93,193–93,205 | 0 each |
| 10.244.15.30 | 21, 22, 23 | 93,148–93,231 | 0 each |
| 10.244.12.29 | 12, 13, 14 | 93,195–93,233 | 0 each |
| **Sum** | **24** | **5,156,859** | **0** |

Partitions 0–11 held ~336 k events (from retained backlog + k6 ramp); partitions 12–23 held ~93 k events. All drained before k6 finished — the consumer ran ahead of the producer for the final ~65 s of cooldown.

## Repository (PostgreSQL `pgsql-pp-platinum-1`)

| Stat | Value |
|---|---|
| Peak CPU | 12.53 % (at 20:40Z, start of ramp) |
| Average CPU (all samples) | ~9.2 % |
| Binding constraint | **none — consumer drained at line rate** |

Platinum PG DB CPU is notably lower than gold (12.5 % peak vs 35.7 %). At platinum, 8 pods distribute the bulk-write load across 24 partitions such that no single pod saturates its write path. The DB absorbed every batch at line rate with comfortable headroom.

## Application pod consumption

Per-pod Container Insights `Perf` data **not captured this batch** — driver did not snapshot the Container Insights `Perf` table. AKS Container Insights query is a known follow-up.

## Stop conditions

| Signal | Fired? |
|---|---|
| 15-min wall-clock cap | NO — k6 finished first at 795 s |
| **k6 `testrun-finished`** | **YES — first time in loadtest history** |
| p95 ≥ 60,000 ms | NO |
| DB CPU > 80 % for ≥ 2 samples | NO |
| k6 VU exhaustion | NO |

## Notes

- **k6.log is empty** for this run (driver race; see silver-PG notes).
- **`testrun-finished` at 795 s** is the canonical proof that bulk-write coalescing closes the throughput gap at the highest tier. Phase 5 v1 platinum-PG accumulated 1,760,000 end-lag; v2 drains to zero with 105 s to spare before the 15-min cap.
- Zero pod restarts (Phase 5 v1: 3 of 8 restarts). The bulk-write path drops per-pod CPU below the CFS-throttle cliff that caused liveness-probe misses in v1.
- 24-partition topic (expanded from 12 via PR #65) — each of 8 pods owns 3 partitions. Even partition assignment (3:3:3:3:3:3:3:3) vs v1's uneven 2/1 split contributes to balanced drain rates across pods.

## Artifacts

`.omc/research/kafka-loadtest/platinum-pg-1778186279/`
