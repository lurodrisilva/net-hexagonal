# Kafka Inbound Load Test — Consolidated Summary (PG + DocumentDB)

Phase 5 complete. 6 runs across 3 tiers × 2 repos against the `aks-test` cluster.

| Run date | Run set | Driver | Status |
|---|---|---|---|
| 2026-05-06 | PG silver / gold / platinum | v3 (AI query broken) | ✅ ran to cap; AI p95 recovered post-hoc with corrected query |
| 2026-05-07 | DocDB silver / gold / platinum | v3 + AI-fix + `P95_BREACH_THRESHOLD_MS=60000` override | ✅ ran to cap (silver/gold) or k6 VU-exhaust (platinum) |

DocDB runs used a raised p95-breach threshold so they reach the 15-min cap window for apples-to-apples comparison vs PG runs (which de-facto had no working p95 stop signal). The default `200 ms` threshold trips Mongo runs at ~100 s — useful for catching real degradation in CI but blocks the long-window comparison this batch needed. Threshold is now env-driven (`P95_BREACH_THRESHOLD_MS`) so future runs can pick the right value per scenario.

## Lane isolation (Phase 5 redo, applied to both batches)

| Lane | Pool | Components |
|---|---|---|
| Workload | `nodepool2` (preferred), `nodepool` / `nodepool3` (overflow) | hex-scaffold app pods, wiremock |
| Broker + infra | `nodepool4` only | Strimzi Kafka brokers ×2, cruise-control, entity-operator, kafka-exporter |
| Load gen | `nodepool3` only | k6 TestRun runner pods |

## Per-tier comparison — PG vs DocumentDB

### Throughput + lag

| Tier | Repo | App pods | k6 offered | Effective offered (broker) | Consumed (steady) | End-of-run lag | Producer p95 |
|---|---|---|---:|---:|---:|---:|---:|
| Silver  | PG     | 2 | 414/s   | 414/s | 414/s    | **0**       | 82.6 µs |
| Silver  | Mongo  | 2 | 414/s   | 414/s | ~628/s*  | **146,616** | 84.5 µs |
| Gold    | PG     | 4 | 2,070/s | 2,070/s | ~502/s | **1.42 M**  | 71.8 µs |
| Gold    | Mongo  | 4 | 2,070/s | 2,070/s | ~447/s | **1.48 M**  | 69.1 µs |
| Platinum| PG     | 8 | 3,238/s (+ 253k k6-dropped) | 3,238/s | ~1,248/s | **1.76 M** | 214 µs |
| Platinum| Mongo  | 8 | not captured (k6.log lost) | ~2,683/s** | ~880/s | **1.17 M** | not captured |

*Mongo silver consumed > offered because the topic carried retention from earlier silver-mongo runs on the same topic.
**Platinum-Mongo k6 also hit the 720-VU exhaustion warning at 02:59:15 — the offered ceiling is set by k6, not the workload.

### End-to-end latency (App Insights `inbound_event_processing_duration_ms`)

| Tier | Repo | p50 | p95 | p99 | max | record count |
|---|---|---:|---:|---:|---:|---:|
| Silver  | PG     | (data lost — DNS outage + sampling) | (data lost) | — | — | — |
| Silver  | Mongo  | 6,625 ms  | **34,942 ms** | 35,501 ms | 35,650 ms | 114 |
| Gold    | PG     | (single-percentile diagnostic) | **15,467 ms** | — | — | 180 |
| Gold    | Mongo  | 5,282 ms  | **29,689 ms** | 33,742 ms | 35,589 ms | 234 |
| Platinum| PG     | (single-percentile diagnostic) | **24,328 ms** during run / **33,230 ms** post-drain | — | — | — |
| Platinum| Mongo  | 4,845 ms  | **36,924 ms** | 46,434 ms | 50,774 ms | 369 |

PG numbers are partial because the in-loop AI query was broken until PR #60 — only the in-run diagnostic queries captured anything, and only at single-percentile granularity. Mongo numbers are full p50/p95/p99/max from the corrected query at 2 h post-cap window.

### App pod actual consumption (Container Insights `Perf` table)

Per-pod CPU + memory averaged across all replicas during each run window.

| Tier | Repo | Pods | CPU avg per pod | CPU max (burst) | % of CPU limit | Memory avg | Mem % of limit | Pod restarts during run |
|---|---|---|---:|---:|---:|---:|---:|---:|
| Silver  | PG     | 2 | **622 m**   | 1,778 m | 62 %, **178 % burst** | 240 MiB | 24 % | 0 of 2 |
| Silver  | Mongo  | 2 | **135 m**   | 242 m   | 13 %, 24 % burst       | 135 MiB | 14 % | 0 of 2 |
| Gold    | PG     | 4 | **922 m**   | 2,147 m | 46 %, **107 % burst** | 162 MiB | 8 %  | 1 of 4 |
| Gold    | Mongo  | 4 | **135 m**   | 379 m   | 7 %,  19 % burst       | 135 MiB | 7 %  | 0 of 4 |
| Platinum| PG     | 8 | **1,547 m** | 4,294 m | 39 %, **107 % burst** | 150 MiB | 4 %  | 3 of 8 |
| Platinum| Mongo  | 8 | **148 m**   | 499 m   | 4 %,  12 % burst       | 153 MiB | 4 %  | 0 of 8 |

PG pods burn 4–10× more CPU than Mongo pods at every tier. PG handler is **CPU-active** (EF Core executes the SQL serialization + Postgres protocol round-trip locally). Mongo handler is **CPU-idle, I/O-blocked** (Mongo C# driver async-awaits the DocDB write ack; the .NET thread parks).

PG pods restart under sustained load (1 at gold, 3 at platinum) because the bursts that exceed the CPU limit periodically miss the liveness probe deadline. Mongo pods never restart because they never burst near the limit.

### DB CPU peak

| Tier | Repo | DB CPU peak | DB CPU avg (peak window) |
|---|---|---:|---:|
| Silver  | PG     | 6.94 % | ~5 % |
| Silver  | Mongo  | **53 %**   | ~50 % |
| Gold    | PG     | 10.19 % | ~9.7 % |
| Gold    | Mongo  | 18.6 % | ~16 % |
| Platinum| PG     | 10.94 % | ~9.5 % |
| Platinum| Mongo  | 8.9 % | ~7 % |

DocDB silver is the **only run where the DB itself is the binding constraint** (50 % CPU plateau matched the lag-growth curve). Every other run leaves the DB with 80–95 % headroom — bottleneck is firmly upstream in the consumer handler.

### Per-pod throughput efficiency

| Tier | Repo | Per-pod consume rate | Per-pod CPU avg | events/s/mcore |
|---|---|---:|---:|---:|
| Silver  | PG     | 207 events/s | 622 m   | **0.33** |
| Silver  | Mongo  | 314 events/s* | 135 m | **2.32** |
| Gold    | PG     | 125 events/s | 922 m   | **0.14** |
| Gold    | Mongo  | 112 events/s | 135 m   | **0.83** |
| Platinum| PG     | 156 events/s | 1,547 m | **0.10** |
| Platinum| Mongo  | 110 events/s | 148 m   | **0.74** |

*Mongo silver per-pod rate inflated by retention from earlier silver-mongo runs on the same topic.

Mongo per-mcore numbers are 5–7× higher than PG per-mcore numbers — but that "efficiency" is not productive capacity. The Mongo pods spend most of their wall time idle (parked on `await mongoClient.InsertOneAsync`). PG pods spend the same wall time actively burning CPU on EF Core / Npgsql work. Both arrive at similar end-to-end p95 (15–37 s) at gold/platinum because both are bottlenecked on the per-partition single-thread write loop, just for different reasons.

## What we learned

1. **Both repos are handler-bound at gold/platinum, not DB-bound.** PG (922 m / 1547 m avg pod CPU vs 2 cpu / 4 cpu limit) has CPU headroom; Mongo (135 m / 148 m avg pod CPU vs 2 cpu / 4 cpu limit) is dramatically over-provisioned. In both cases, partition-count = 12 caps consumer parallelism.
2. **Mongo silver is DB-bound** (50 % CPU plateau on `documentdb-silver`). PG silver is not (7 % CPU on `pgsql-pp-silver-1`). The DocDB silver SKU is sized smaller relative to the load this consumer delivers than the PG silver SKU is.
3. **PG is faster per event when the database has headroom.** PG gold p95 = 15.5 s vs Mongo gold p95 = 29.7 s. Same tier, same offered rate, same pod count — Mongo's per-write latency is ~9 ms vs PG's ~8 ms + handler CPU spent locally.
4. **Restart pattern correlates with CPU bursts.** PG pods restart under load (CPU bursts past limit → CFS throttle → liveness probe miss). Mongo pods never restart because they never burst.
5. **Outbound publisher was suppressed in all 6 runs** (`features.outboundAdapter=rest` → `NoOpEventPublisher`). No outbound topic contamination.

## Stop conditions

| Tier | Repo | Cap reached | DB CPU breach | Lag breach (manual) | k6 producer ceiling |
|---|---|---|---|---|---|
| Silver  | PG     | YES | NO | NO (lag = 0) | NO |
| Silver  | Mongo  | YES | NO (peak 53 %) | YES (lag → 147 k) | NO |
| Gold    | PG     | YES | NO | YES (lag → 1.42 M) | NO |
| Gold    | Mongo  | YES | NO | YES (lag → 1.48 M) | NO |
| Platinum| PG     | YES | NO | YES (lag → 1.76 M) | YES (253 k dropped iter) |
| Platinum| Mongo  | NO  | NO | YES (lag → 1.17 M) | YES (720 VU exhaustion) |

## Driver evolution recap

| Driver version | Change | Why |
|---|---|---|
| v1 (Phase 5 first attempt) | baseline | k6 logs lost to delete-before-capture race; AI count=0 |
| v2 | added `kubectl cp /tmp/summary.json` BEFORE delete; added 3 AI diagnostic queries | `kubectl cp` failed on Completed pods; DNS outage broke all `az` calls |
| v3 | replaced `kubectl cp` with `kubectl logs` (works on Completed pods); wrapped all `az` + `kubectl` calls in `gtimeout` 15–25 s and `--request-timeout` | proven on PG runs — clean artifact capture |
| **v3 + PR #60 AI-fix** | corrected `customDimensions.runId` filter → `tier + repo` filter | runId is an Activity tag, not a metric tag; old query returned count=0 |
| **v3 + AI-fix + threshold env-var** (current) | `P95_BREACH_THRESHOLD_MS` exposed as env override (default 200 ms) | Mongo p95 trips threshold within ~100 s; raise to e.g. 60 s for cap-window comparison runs |

## Phase 6 deliverable status

| Item | Status |
|---|---|
| Per-run reports for 3 PG tiers | ✅ `reports/loadtest-run-2026-05-06-postgresql-{silver,gold,platinum}-kafka.md` |
| Per-run reports for 3 DocDB tiers | ✅ `reports/loadtest-run-2026-05-07-documentdb-{silver,gold,platinum}-kafka.md` |
| Consolidated PG-only summary | ✅ `reports/loadtest-kafka-summary-postgresql-2026-05-06.md` |
| Consolidated PG + DocDB summary | ✅ this file |
| App Insights p95 column populated for all tiers | ⚠️ partial (PG has single-percentile diagnostics only; Mongo has full p50/p95/p99/max) — `ai-final` post-run sweep follow-up will close this |

## Recommended Phase 6 follow-ups

1. **Profile the consumer handler** — per-pod ceiling at ~110–200 events/s warrants tracing per stage:
   - Kafka deserialize → Mediator dispatch → repository write → offset commit
   - Compare PG (CPU-active) vs Mongo (I/O-wait) per-stage breakdowns.
2. **Bulk write coalescing** — both repos serialise per-event. Batching N events per `SaveChangesAsync` (PG) or `InsertManyAsync` (Mongo) at the consumer-batch boundary should multiply per-pod throughput.
3. **Increase topic partitions to 16 or 24** — current 12 caps consumer parallelism; expanding lets pods 8 (platinum) own 2–3 partitions evenly.
4. **Scale k6 producer for Platinum** — `maxVUs=720` exhausts on both PG and Mongo platinum. Either raise to ≥ 1500 or run parallel TestRunners.
5. **Add `lag-growth-monotonic` stop rule** to driver — would end Gold/Platinum runs immediately on lag-saturation instead of running to cap.
6. **Add `ai-final` post-run sweep** — App Insights ingest lag is 5–15 min, so the in-driver `ai-final.json` query often fires before records arrive. A separate post-cap sleep + re-query would capture the consolidated p95 reliably.
7. **Resize DocDB silver upward** — only DocDB silver hit the DB-bound regime (53 % CPU). If silver is meant to represent a realistic small-tenant SKU, that's expected; if it's meant to mirror PG silver headroom, the SKU needs +1 size.
8. **Re-test PG with corrected AI query end-to-end** — Re-running the 3 PG tiers under the v3+AI-fix driver would yield comparable p50/p95/p99/max against the Mongo set (current PG set has only single-percentile diagnostics).
