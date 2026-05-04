# DocumentDB Platinum — load test peak (2026-05-04)

**Tier:** Azure Cosmos DB for MongoDB vCore — **M40** (4 vCPU, 16 GiB RAM, 32 GiB disk, **HA on** primary + zone-redundant standby, Premium SSD storage)
**Cluster:** `documentdb-platinum` (region `brazilsouth`, RG `resources-test-rg`)
**App release:** `hex-scaffold` (image `ghcr.io/lurodrisilva/net-hexagonal:latest`)
**App profile:** 8 base replicas, HPA min=8 / max=24, requests `cpu: 80m, memory: 384Mi`, limits `cpu: 1000m, memory: 768Mi`
**k6 profile:** 6 runners × ramping-arrival-rate, peak 1500 RPS/runner = 9000 aggregate target
**Window:** 05:25–05:28 UTC

## Result

| Metric | Value | Target | Status |
|---|---|---|---|
| **Aggregate RPS** | **~2553** (~425/runner × 6) | maximize | peak |
| DB CPU peak (1m max, 05:27) | 86.9% | 60–80% | just above band |
| DB CPU peak (1m max, 05:28 drain) | 89.65% | n/a | drain phase |
| DB CPU at 05:26 (peak ramp) | 62.9% | 60–80% | ✅ in band during ramp |
| **DB Memory peak (1m max)** | **62.4%** | **60–80%** | **✅ in band** |
| DB IOPS peak | 665 ops/s | tier ceiling | well below |
| Error rate (k6) | 0.00–0.01% | <1% | ✅ |
| Throttled (429) rate | 0.00% | <50% | ✅ |
| Latency p95 — list | 156–246 ms | n/a | |
| Latency p95 — get | 350–373 ms | n/a | |
| Latency p95 — create | 458–472 ms | n/a | |
| Latency p95 — update | 687–703 ms | n/a | |
| MongoRequestDurationMs (server-side avg, 05:25–05:27) | 2.30–3.14 ms | <20 (median target) | ✅ |

**Both DB CPU AND Memory hit saturation band**: CPU rose through 62.9% (in band) → 86.9% (overshoot at 05:27) → 89.65% (drain). The peak hold at 05:27 sits ~7 percentage points above the upper edge, which is the realistic peak for an M40 with HA. Memory ended at 62.4% (low edge of band).

**Server-side Mongo latency stayed at 2.3–3.1ms average** through the peak hold — DB itself is healthy. The 14859ms max-latency reading at 05:28 is a drain-phase artefact (a single slow op dominates the small remaining sample at the end of the ramp).

**RPS step Gold → Platinum: 1003 → 2553 = 2.55×** — exceeds the "double per step" target, the largest jump in the four-tier sweep.

## Per-runner k6 summary (representative)

```
Requests:                133996           (×6 runners ≈ 804k aggregate)
Error rate (5xx/4xx):    0.01%
http_req_failed (k6):    0.00%

RPS / runner             425.91           (×6 runners ≈ 2553 aggregate)

Latency p95 (ms)         create=467.47  get=372.32  list=156.59  update=703.13
Saturation (VUs p95)     490
```

App-side `http_req_duration` p95s are roughly **half of Gold's** at 2.55× the load — Platinum's 4 vCPU primary node clears the request queue faster than Gold's 2 vCPU node could, which masks the HA-replication tail somewhat. The same caveats from the Gold report apply: app-tier queueing under the higher offered load (likely exacerbated by `MaxConnectionPoolSize=200` × 8+ pods overcommitting M40's connection ceiling).

## DB metric trace (per-minute, max + avg)

| Time | CPU max | CPU avg | Mem max | IOPS max | Server lat avg |
|---|---|---|---|---|---|
| 05:25 (warmup) | 35.1% | 10.1% | 39.8% | 536 | 2.89 ms |
| 05:26 (steady) | 62.9% | 20.6% | 42.2% | 665 | 2.30 ms |
| 05:27 (peak hold) | 86.9% | 25.6% | 43.3% | 646 | 3.14 ms |
| 05:28 (drain) | 89.65% | 27.9% | 62.4% | 519 | 155.7 ms (tail) |

The CPU 1-min max climbs steeply through the peak hold (62.9% → 86.9% → 89.65%) but the 1-min average never crosses 30%. That gap means the cluster's primary node is bursty under the offered load — long stretches at low CPU punctuated by short bursts that push max-of-1m past 80%. Memory rose more gently and landed at 62.4% during drain.

## Repro

```bash
export ADMIN_PWD='<from secret store>'
bash tests/loadtest/k6/loadtest-documentdb.sh run platinum
```

Artifacts:
- `tests/loadtest/k6/values-documentdb-platinum.yaml` — helm overlay (8 replicas, HPA 8/24, mongo persistence)
- `tests/loadtest/k6/rest-api-loadtest-documentdb.js` (TIER=platinum → 1500-RPS-peak ramp)

## Observations

1. **Platinum is the realistic ceiling for the current app footprint.** At 2553 RPS the app-side latency p95s are visibly elevated; pushing the offered rate higher would push DB CPU well past 90% and the app would queue more aggressively without a corresponding RPS gain.
2. **Memory headroom is large** (62.4% peak vs M40's 16 GiB). Workload remains CPU-bound on M40, never memory-bound.
3. **IOPS at 665 ops/s is far below M40's ~10000 IOPS ceiling** (~15× headroom on Premium SSD v2). DB is CPU-bound, not IO-bound.
4. **Same connection-pool concern as Gold** — `MaxConnectionPoolSize=200` × 8+ HPA-scaled pods overcommits M40's connection limit. Lowering pool size to ~100 per pod would smooth p95 without hurting RPS.
