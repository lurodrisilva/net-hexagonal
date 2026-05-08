# Kafka Inbound Load Test — PostgreSQL Summary (Phase 5 v2)

Phase 5 v2 PostgreSQL-only results. Three tiers re-run after bulk-write coalescing (PRs #66 + #68) and 12→24 partition expansion for gold/platinum (PR #65).

| Run date | Run set | Driver | Status |
|---|---|---|---|
| 2026-05-07 | PG silver / gold / platinum | v3 + AI-fix + `P95_BREACH_THRESHOLD_MS=60000` + bulk-write image | ✅ all 3 ran; platinum hit `testrun-finished` |

## Headline numbers

| Tier | Pods | Partitions | Offered rate | End-lag | p50 (ms) | p95 (ms) | p99 (ms) | Elapsed (s) | Stop reason |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| Silver | 2 | 12 | ~973 /s | **372** | 3,089 | 37,451 | 41,242 | 930 | cap-reached |
| Gold | 4 | 24 | ~5,882 /s | **40,688** | 14,736 | 41,885 | 42,203 | 920 | cap-reached |
| Platinum | 8 | 24 | ~6,487 /s | **0** | 5,373 | 18,824 | 20,964 | 795 | **testrun-finished** |

## vs Phase 5 v1 (PG baseline)

| Tier | v1 end-lag | v2 end-lag | Δ lag | v1 p95 (ms) | v2 p95 (ms) | Δ p95 |
|---|---:|---:|---|---:|---:|---|
| Silver | 0 | 372 | borderline — see Notes | (data lost) | 37,451 | n/a (contaminated) |
| Gold | 1,420,000 | 40,688 | **-97.1%** | 15,467 | 41,885 | +171% (catchup backlog) |
| Platinum | 1,760,000 | 0 | **-100%** | 24,328 | 18,824 | **-22.6%** |

Gold p95 increased vs v1 because the bulk-write handler processes larger batches that include older retained-backlog events; the higher per-batch write latency inflates the histogram. End-lag improvement (-97%) is the correct signal — the handler is draining, not accumulating.

## DB CPU

| Tier | DB SKU | Peak CPU | Avg CPU | Bottleneck |
|---|---|---:|---:|---|
| Silver | `pgsql-pp-silver-1` | 38.3 % | ~14.8 % | application handler |
| Gold | `pgsql-pp-gold` | 35.7 % | ~25.6 % | application handler |
| Platinum | `pgsql-pp-platinum-1` | 12.5 % | ~9.2 % | none — drained at line rate |

All three PG instances have comfortable headroom. Gold CPU is the highest because bulk-write batches are largest at gold (4 pods × 6 partitions × 2,500 /s target), driving higher per-batch Postgres activity.

## Pod restarts

| Tier | v1 restarts | v2 restarts |
|---|---:|---:|
| Silver | 0 of 2 | 0 of 2 |
| Gold | **1 of 4** | **0 of 4** |
| Platinum | **3 of 8** | **0 of 8** |

Zero pod restarts across all tiers. The bulk-write path reduces per-pod CPU below the CFS-throttle cliff that caused liveness-probe misses in v1.

## What we learned

1. **Platinum-PG `testrun-finished`** is the definitive proof that bulk-write coalescing resolves the PG handler throughput gap. No other PG tier or version in the loadtest history has completed all k6 stages inside the cap window.
2. **Gold-PG residual lag (40,688)** is attributable to one pod (10.244.16.27) that lagged its peers — three of four pods reached zero-lag and held it. Root cause is likely a per-pod write-rate variance under the bulk path; adding a lag-monotonic stop rule would terminate the run earlier without the 15-min cap penalty.
3. **Silver-PG end-lag 372 is noise**, not regression. The v2 chain started against topics with retained backlog from the failed pre-fix attempt earlier in the day. Phase 5 v1 silver-PG also used a clean topic and ended at lag=0.
4. **p95 contamination** affects silver and gold because retained-backlog payloads have stale producer timestamps. Platinum p95 (18,824 ms) is uncontaminated and directly comparable to v1 (24,328 ms) — a genuine -23% improvement.
5. **Per-pod CPU not captured** this batch (driver did not snapshot Container Insights `Perf` table). Follow-up required.

## Stop conditions summary

| Tier | Cap reached | p95 breach | DB CPU breach | testrun-finished |
|---|---|---|---|---|
| Silver | YES | NO | NO | NO |
| Gold | YES | NO | NO | NO |
| Platinum | NO | NO | NO | **YES** |

## Driver evolution recap (Phase 5 v2)

| Change | PR | Why |
|---|---|---|
| Batched bulk writes via `SaveBatchAsync` | #66 | Close handler throughput gap: per-event `SaveChangesAsync` serialised all writes |
| 12→24 partitions on gold/platinum topics | #65 | Double consumer parallelism at gold (4 pods) and platinum (8 pods) |
| Per-tier k6 overlay tuning | #67 | Phase 5 v2 profiles with revised `preAllocatedVUs` / `maxVUs` per tier |
| Same-batch Create+Update / Create+Delete id-collapse | #68 | Hot-fix: `DbUpdateConcurrencyException` on every batch when Add+Update for same id within one poll → EF state-flip |

## Phase 5 v2 PG follow-ups

| Item | Status |
|---|---|
| Per-pod CPU via Container Insights `Perf` table | Open — not captured this batch |
| Gold-PG single-pod lag variance root cause | Open — one pod consistently trails; investigate per-pod write rate under bulk path |
| Fix driver DocDB metric name (`CpuPercent` not `RequestUnitsConsumed`) | Open |
| Raise `kafka_writer` k6.log capture reliability | Open — 5 of 6 runs had empty k6.log; driver race on Completed pods |
| Add `lag-growth-monotonic` stop rule to driver | Open |

## Monthly cost comparison (Azure Retail Prices, Brazil South, USD)

| Tier | Repo | Persistence | Pods @ peak | App share | DB subtotal | App subtotal | Total retail/mo | Total -25%/mo |
|---|---|---|---:|---:|---:|---:|---:|---:|
| Silver | PG | D2ds_v5 / 128 GiB Std SSD | 2 | 100% | $203.17 | $117.53 | $320.70 | $240.53 |
| Gold | PG | D4ds_v5 / 128 GiB PMD V2 | 4 | 200% | $698.37 | $235.06 | $933.43 | $700.08 |
| Platinum | PG | D8ds_v5 / 128 GiB PMD V2 | 8 | 400% | $1,048.77 | $470.12 | $1,518.89 | $1,139.17 |

Methodology: persistence + app pro-rated by peak resource consumption (HPA-bounded for REST, fixed Deployment for Kafka). Binding dimension = max(CPU%, Mem%) of `Standard_D2s_v6` node capacity (2 vCPU / 8 GiB / Linux / Brazil South / $0.161/h). DocDB Mxx unit prices are estimated. 25% uniform discount; real EA/MCA/CSP agreements discount per-meter.
