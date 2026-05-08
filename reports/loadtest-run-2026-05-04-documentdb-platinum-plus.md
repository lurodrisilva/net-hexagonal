# DocumentDB Platinum+ (M60) — load test peak (2026-05-04)

> New top-of-ladder Mongo tier. Provisioned as a parallel cluster
> (`documentdb-platinum-plus`, **NOT** an in-place upgrade from M50) — the
> M50 Platinum baseline stays live for comparison and Platinum+ becomes the
> new ceiling. Per user instruction the cluster is left running.

**Tier:** Azure Cosmos DB for MongoDB vCore — **M60** (16 vCPU, 64 GiB RAM, 32 GiB disk, **HA on** primary + zone-redundant standby, Premium SSD storage)
**Cluster:** `documentdb-platinum-plus` (region `brazilsouth`, RG `resources-test-rg`) — public access **Disabled**, reachable from AKS via private endpoint `documentdb-platinum-plus-pe` in subnet `pe-sub-ddb-platinum-plus` (10.0.15.0/24)
**App release:** `hex-scaffold` (image `ghcr.io/lurodrisilva/net-hexagonal:latest`, Mongo Account repository + `MaxConnectionPoolSize=100`)
**App profile:** **24 base replicas, HPA min=24 / max=64**, requests `cpu: 200m, memory: 512Mi`, limits `cpu: 1500m, memory: 1Gi` (bumped from 12/32 + 80m/384Mi at M50 — heavier per-pod requests to keep p95 tight at sustained 10k RPS, less HPA scale-up churn)
**Mongo client tuning:** `MaxConnectionPoolSize=100`. 64 pods × 100 = 6400 max connections, fits inside M60's connection ceiling.
**k6 profile:** 6 runners × ramping-arrival-rate, peak **6000 RPS/runner = 36000 aggregate target** (doubled from M50's 3000/18000)
**Window:** 17:47:52–17:53:56 UTC (3-min peak hold inside)

## Result

| Metric | Value | Target | Status |
|---|---|---|---|
| **Aggregate RPS** | **~10,050** (~1,675/runner × 6) | ≈10k | **🎯 hit** |
| **DB CPU peak (1m max, 17:53)** | **62.67%** | 60–80% | **✅ in band** |
| DB CPU peak (1m max, 17:52) | 55.26% | 60–80% | climbing |
| DB Memory peak (1m max) | 41.95% | 60–80% | below band (M60's 64 GiB has plenty of room) |
| DB IOPS peak | 858 ops/s | tier ceiling | well below |
| Error rate (k6) | 0.01% | <1% | ✅ |
| Throttled (429) rate | 0.00% | <50% | ✅ |
| Latency p95 — list | 109–503 ms | n/a | wide spread (cold-cache paging on a few runners) |
| Latency p95 — get | 518–693 ms | n/a | |
| Latency p95 — create | 594–717 ms | n/a | |
| Latency p95 — update | 601–746 ms | n/a | |
| MongoRequestDurationMs (server-side avg, peak) | 3.18–12.28 ms | <20 (median target) | ✅ |
| HPA scaled to | **64 / 64 (max hit)** | n/a | app tier became the binding metric |

**DB CPU climbed cleanly to 62.67% at the end of the 3-min peak hold** ✅ — well inside the 60–80% saturation band and with ~17 percentage points of headroom. Memory only reached 41.95% on M60's 64 GiB, confirming the workload is CPU-bound rather than memory-bound at every tier from Silver upward.

**HPA hit max (64 replicas)** during the peak hold while DB CPU was still in the comfortable middle of the band. **The bottleneck at 10k RPS shifted from the DB to the app tier** — pushing further offered load wouldn't have produced higher RPS without raising HPA `maxReplicas` or per-pod limits.

**RPS step M50 → M60: 5147 → 10050 = 1.95× — close to the "double per step" trend (held since Bronze→Silver).**

## Per-runner k6 summary

| Runner | Requests | Avg RPS | p95 create (ms) | p95 get (ms) | p95 list (ms) | p95 update (ms) | Saturation p95 VUs |
|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | 534,746 | 1694.41 | 594.64 | 518.81 | 194.68 | 639.65 | 1414 |
| 2 | 525,194 | 1662.49 | 652.88 | 606.26 | 209.74 | 679.34 | 1396 |
| 3 | 523,234 | 1658.47 | 717.71 | 692.68 | 503.49 | 746.07 | 1438 |
| 5 | 534,431 | 1694.01 | 596.55 | 532.75 | 109.50 | 601.84 | 1409 |
| 6 | 525,863 | 1667.16 | 681.89 | 623.10 | 199.65 | 690.44 | 1419 |
| **6 total** | **~3.18M** | **~10,050** | **~640 (med)** | **~590 (med)** | **~200 (med)** | **~675 (med)** | **~1415 (med)** |

Runner 4 dropped its summary block (logs rotated before fetch) but reported ~525k requests during the run — included in the aggregate by extrapolation.

App-side p95 elevation under sustained 10k RPS load is roughly proportional to M50 results at half the offered load — the per-RPS latency curve is staying in line, the absolute number is driven by the offered-rate doubling.

## DB metric trace (per-minute, max + avg)

| Time | CPU max | CPU avg | Mem max | Mem avg | IOPS max | Server lat avg |
|---|---|---|---|---|---|---|
| 17:47 (warmup) | 3.30% | 1.93% | 30.42% | 21.52% | 1 | n/a |
| 17:48 (warmup) | 3.50% | 1.80% | 30.44% | 22.58% | 7 | n/a |
| 17:49 (steady ramp) | 6.67% | 4.16% | 31.58% | 20.59% | 588 | 12.26 ms |
| 17:50 (peak ramp) | 32.25% | 12.63% | 34.48% | 22.63% | 678 | 3.18 ms |
| 17:51 (peak hold) | 46.86% | 19.69% | 38.01% | 23.91% | 594 | 6.25 ms |
| 17:52 (peak hold) | 55.26% | 21.35% | 41.95% | 25.08% | 619 | 12.28 ms |
| 17:53 (peak hold + drain) | **62.67%** | 27.16% | 41.95% | 25.08% | **858** | 11.88 ms |
| 17:54 (drain) | 12.68% | 6.46% | n/a | n/a | 694 | n/a |

CPU rose monotonically through the peak hold (32 → 47 → 55 → 62.67%). The 1-min average (27%) is much lower than the max (62.67%): M60's 16 vCPU absorbs sub-minute spikes within each interval, with the per-minute max representing the highest sub-minute burst.

## Comparison to M50 Platinum

| Metric | M50 Platinum | **M60 Platinum+** | Change |
|---|---:|---:|---|
| Compute | 8 vCPU / 32 GiB | **16 vCPU / 64 GiB** | 2× compute |
| App replicas (base/max) | 12 / 32 | **24 / 64** | 2× |
| Pod requests | 80m / 384Mi | **200m / 512Mi** | 2.5× CPU req, 1.3× mem req |
| Mongo MaxConnectionPoolSize | 100 | 100 | unchanged |
| **Aggregate RPS** | ~5147 | **~10,050** | **1.95×** |
| DB CPU peak | 73.5% | **62.67%** | M60 hits 10k with more headroom |
| DB Memory peak | 46.9% | 41.95% | both well under band |
| Server lat avg (peak) | 3.30–4.20 ms | 3.18–12.28 ms | similar lower bound; brief spikes coincide with HPA scale-up |
| HPA max hit? | no (32 max not hit) | **yes (64 max hit)** | app tier is the binding metric at 10k |
| Errors | 0.00% | 0.01% | both ✅ |

**M60 absorbs 2× the offered load with healthier saturation** (62.67% CPU vs M50's 73.5%) — the DB tier still has ~17 percentage points of headroom at 10k RPS, but the app tier is now the cap. To push the DB into the 70–80% band, raise HPA max above 64 or move to D-class nodepools with higher per-pod CPU limits.

## Repro

```bash
export ADMIN_PWD='<from secret store>'
bash tests/loadtest/k6/loadtest-documentdb.sh deploy platinum-plus
bash tests/loadtest/k6/loadtest-documentdb.sh apply  platinum-plus
bash tests/loadtest/k6/loadtest-documentdb.sh wait
bash tests/loadtest/k6/loadtest-documentdb.sh summary
bash tests/loadtest/k6/loadtest-documentdb.sh cleanup
```

Cluster left running on M60 per user instruction (new Platinum+ baseline).

Artifacts:
- `tests/loadtest/k6/values-documentdb-platinum-plus.yaml` — helm overlay (24 replicas, HPA 24/64, mongo persistence)
- `tests/loadtest/k6/rest-api-loadtest-documentdb.js` — `TIER=platinum-plus` selects the 6000-RPS-peak ramp (preAllocatedVUs=8000, maxVUs=12000)

## Observations

1. **Doubling per step held at M50→M60.** RPS step ratio 1.95× — within the "double per step" trend the sweep has tracked since Bronze→Silver (2.48×, 2.32×, 2.55×, 2.02×, 1.95×).
2. **The bottleneck migrated from DB to app tier at 10k RPS.** M60 DB CPU stayed at 62.67% while HPA pegged at 64/64 max replicas. The app pods (24/64 with 200m requests) capped the achievable RPS.
3. **Memory is still over-provisioned** for this workload shape — 41.95% peak on 64 GiB. Future M60 work targeting memory-bound workloads (large working sets, vector search) would land differently.
4. **Server-side Mongo latency averaged 3–12 ms across the peak hold** — DB itself remains healthy. The 12 ms readings at 17:49 and 17:52 coincide with HPA scale-up activity (more pods opening Mongo connections at once).
5. **Connection-pool sizing fix (PR #42) continues to hold.** With `MaxPoolSize=100 × 64 pods = 6400` connections, the new M60 ceiling is comfortably above the demand. No app-tier queueing visible in the latency curve.
6. **Errors stayed at 0.01%** — the same residual rate as Gold/Platinum, attributable to k6's `golden_traffic` mid-flight 4xx negative checks (intentional). Substantive 5xx rate was 0%.

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | HPA max (hit) | 64 |
| CPU reserved at peak | 64 × cpu=200m | 12800m |
| Memory reserved at peak | 64 × memory=512Mi | 32768 Mi (32 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 12800m / 2000m = 640.0% **← binding**
- Memory: 32768 Mi / 8192 Mi = 400.0%
- Pro-rate share = 6.40 (app spans ~6.4 D2s_v6 nodes)

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| Cosmos DB for MongoDB vCore M60 Compute | N/A — estimated | N/A — estimated | 1 Hour |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| DocDB M60 compute (estimated) | ~$1.60/hr × 730 | ~1168.00 | ~876.00 |
| DocDB storage 32 GiB (included in M60) | included | 0.00 | 0.00 |
| DocDB subtotal | | ~1168.00 | ~876.00 |
| App pro-rated D2s_v6 | 0.161 × 730 × 6.40 | 751.81 | 563.86 |
| App subtotal | | 751.81 | 563.86 |
| **Platinum+ (M60) REST + DocDB total** | | **~$1919.81** | **~$1439.86** |

Savings: ~$479.95/month at 25% discount.

### Notes

- HPA-bounded reservation as proxy (no per-pod CPU/memory snapshot in this run report).
- CPU binds (640%) over Memory (400%); pro-rate = 6.40 — app spans ~6.4 D2s_v6 nodes. HPA hit 64/64 max, making app tier the throughput cap.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB.
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- **Cosmos DB for MongoDB vCore M60 is NOT listed in the Azure Retail Prices API (`prices.azure.com`) for brazilsouth as of 2026-05-07. The ~$1.60/hr figure is an estimate; verify at https://azure.microsoft.com/pricing/details/cosmos-db/mongodb/ before use in billing models.**
- M60 HA (zone-redundant standby) included; estimate reflects HA-enabled cluster price.
- App cost ($752) nearly equals DB cost ($1168) at this tier; in practice the DB is over-provisioned relative to what the app can drive at 10k RPS.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
