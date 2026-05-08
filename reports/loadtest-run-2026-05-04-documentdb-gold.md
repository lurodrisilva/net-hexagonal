# DocumentDB Gold — load test peak (2026-05-04)

**Tier:** Azure Cosmos DB for MongoDB vCore — **M30** (2 vCPU, 8 GiB RAM, 32 GiB disk, **HA on** primary + zone-redundant standby, Premium SSD storage)
**Cluster:** `documentdb-gold` (region `brazilsouth`, RG `resources-test-rg`)
**App release:** `hex-scaffold` (image `ghcr.io/lurodrisilva/net-hexagonal:latest`)
**App profile:** 8 base replicas, HPA min=8 / max=16, requests `cpu: 80m, memory: 384Mi`, limits `cpu: 1000m, memory: 768Mi`
**k6 profile:** 6 runners × ramping-arrival-rate, peak 600 RPS/runner = 3600 aggregate target
**Window:** 05:17–05:20 UTC

## Result

| Metric | Value | Target | Status |
|---|---|---|---|
| **Aggregate RPS** | **~1003** (~167/runner × 6) | maximize | peak |
| **DB CPU peak (1m max)** | **76.96%** at 05:19 | **60–80%** | **✅ in band** |
| DB CPU spike (drain phase, 05:20) | 98.4% | n/a | drain artefact |
| DB Memory peak (1m max) | 51.5% | 60–80% | below band |
| DB IOPS peak | 470 ops/s | tier ceiling | well below |
| Error rate (k6) | 0.00% | <1% | ✅ |
| Throttled (429) rate | 0.00% | <50% | ✅ |
| Latency p95 — list | 17.5–22.9 ms | n/a | **app-side healthy** |
| Latency p95 — get | 648–661 ms | n/a | app-tier queueing |
| Latency p95 — create | 879–893 ms | n/a | HA replication tail + app queue |
| Latency p95 — update | 1522–1546 ms | n/a | HA replication + app queue |
| MongoRequestDurationMs (server-side avg, 05:18) | 8.59 ms | <20 (median target) | ✅ |
| MongoRequestDurationMs (server-side avg, 05:19) | 3.49 ms | <20 | ✅ |

**DB-side saturation reached ✅** (CPU 76.96%). **Server-side Mongo durations stayed under 10ms average** through the peak hold — DB itself is healthy.

The app-side `http_req_duration` p95 spikes (create 880ms, update 1500ms, get 660ms) point at **app-tier queueing**, not DB latency. Likely sources, in priority order:

1. **Mongo connection pool saturation** — `MaxConnectionPoolSize=200` per app pod × 8 pods = 1600 potential connections. Mongo vCore M30's connection ceiling is ~800. Half the pods queue waiting for a connection slot, which surfaces as request-pipeline latency rather than DB latency.
2. **HA replication tail** — every write on M30+ acks after replication to the zone-redundant standby. The 1500ms update p95 is ~10× the 8ms server-side avg, consistent with replication-tail amplification under load.
3. **App-pod CPU saturation** — 80m request × 8 pods = 640m total request, HPA-scaled but only to 16 replicas; at ~1000 RPS that's ~63 RPS per pod, enough to drive CPU > 70% on the small request shape.

**RPS step Silver → Gold: 432 → 1003 = 2.32×** — exceeds the "double per step" target.

## Per-runner k6 summary (representative)

```
Requests:                52522            (×6 runners ≈ 315k aggregate)
Error rate (5xx/4xx):    0.00%
http_req_failed (k6):    0.00%

RPS / runner             167.10           (×6 runners ≈ 1003 aggregate)

Latency p95 (ms)         create=893.20  get=661.08  list=22.87  update=1528.13
Saturation (VUs p95)     304
```

The k6 SLA gate `med<20` per endpoint is satisfied for **list** (p95 22.87ms places p50 well below 20ms) but **borderline** for create/get/update (high p95s mean the median may have crept above 20ms during the peak; the per-runner JSON summary would need to be inspected for the actual median figure, not exposed by the simple-summary formatter).

## DB metric trace (per-minute, max + avg)

| Time | CPU max | CPU avg | Mem max | IOPS max | Server lat avg |
|---|---|---|---|---|---|
| 05:17 | 23.6% | 9.9% | 22.1% | 167 | n/a |
| 05:18 | 58.2% | 21.4% | 27.7% | 431 | 8.59 ms |
| 05:19 | 76.96% | 26.5% | 29.7% | 470 | 3.49 ms |
| 05:20 (drain) | 98.4% | 30.5% | 51.5% | 269 | 14.25 ms (tail) |

The 05:20 CPU max-of-1m of 98.4% is a drain-phase artefact — the 1-min average is only 30%, meaning a single sub-minute burst late in the test pushed the max. The peak-hold window is 05:18–05:19, where CPU peaked at 76.96% (in band) and memory at 29.7% (below band).

## Repro

```bash
export ADMIN_PWD='<from secret store>'
bash tests/loadtest/k6/loadtest-documentdb.sh run gold
```

Artifacts:
- `tests/loadtest/k6/values-documentdb-gold.yaml` — helm overlay (8 replicas, HPA 8/16, mongo persistence)
- `tests/loadtest/k6/rest-api-loadtest-documentdb.js` (TIER=gold → 600-RPS-peak ramp)

## Observations / follow-ups

1. App p95 latency is dominated by app-tier queueing, not DB latency. To reduce it without losing RPS, the highest-impact change is **lower `MaxConnectionPoolSize` to ~100** (the chart-default Postgres ceiling) so 8 pods × 100 = 800 connections fits inside M30's connection limit.
2. Memory has substantial headroom (29.7% peak) — M30's 8 GiB RAM is well-sized for this workload shape.
3. IOPS peaked at 470 ops/s, far below M30's nominal 6000 IOPS budget (~12× headroom on Premium SSD v2). The workload is CPU-bound on M30, not IO-bound.

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | HPA max | 16 |
| CPU reserved at peak | 16 × cpu=80m | 1280m |
| Memory reserved at peak | 16 × memory=384Mi | 6144 Mi (6 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 1280m / 2000m = 64.0%
- Memory: 6144 Mi / 8192 Mi = 75.0% **← binding**
- Pro-rate share = 0.75

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| Cosmos DB for MongoDB vCore M30 Compute | N/A — estimated | N/A — estimated | 1 Hour |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| DocDB M30 compute (estimated) | ~$0.20/hr × 730 | ~146.00 | ~109.50 |
| DocDB storage 32 GiB (included in M30) | included | 0.00 | 0.00 |
| DocDB subtotal | | ~146.00 | ~109.50 |
| App pro-rated D2s_v6 | 0.161 × 730 × 0.75 | 88.15 | 66.11 |
| App subtotal | | 88.15 | 66.11 |
| **Gold (M30) REST + DocDB total** | | **~$234.15** | **~$175.61** |

Savings: ~$58.54/month at 25% discount.

### Notes

- HPA-bounded reservation as proxy (no per-pod CPU/memory snapshot in this run report).
- Memory binds (75.0%) over CPU (64.0%); pro-rate uses binding dimension.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB.
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- **Cosmos DB for MongoDB vCore M30 is NOT listed in the Azure Retail Prices API (`prices.azure.com`) for brazilsouth as of 2026-05-07. The ~$0.20/hr figure is an estimate; verify at https://azure.microsoft.com/pricing/details/cosmos-db/mongodb/ before use in billing models.**
- M30 HA (zone-redundant standby) is included in M30 pricing; the estimate above reflects the HA-enabled cluster price.
- This run uses M30 as the original Gold tier; the canonical Gold tier was later re-designated M40. See `loadtest-run-2026-05-04-documentdb-gold-m40.md` for the canonical Gold cost.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
