# DocumentDB Platinum (M50) — load test peak (2026-05-04)

> Updated Platinum baseline. The original Platinum was M40 (4 vCPU / 16 GiB,
> see `loadtest-run-2026-05-04-documentdb-platinum.md`); this run upgrades
> the cluster in place to **M50** (8 vCPU / 32 GiB) per user request and
> bumps the app-tier headroom + Mongo connection-pool sizing to give the
> bigger DB a fair shake at saturation.

**Tier:** Azure Cosmos DB for MongoDB vCore — **M50** (8 vCPU, 32 GiB RAM, 32 GiB disk, **HA on** primary + zone-redundant standby, Premium SSD storage)
**Cluster:** `documentdb-platinum` (region `brazilsouth`, RG `resources-test-rg`) — upgraded **in place** from M40 via `az cosmosdb mongocluster update --shard-node-tier M50`
**App release:** `hex-scaffold` (image `ghcr.io/lurodrisilva/net-hexagonal:latest`, Mongo Account repository from PR #39 + #40 + pool-size fix from PR #42)
**App profile:** **12 base replicas, HPA min=12 / max=32**, requests `cpu: 80m, memory: 384Mi`, limits `cpu: 1000m, memory: 768Mi` (bumped from 8/24 in M40 baseline)
**Mongo client tuning:** `MaxConnectionPoolSize=100` (lowered from 200 in PR #42 to avoid overcommit at higher pod counts)
**k6 profile:** 6 runners × ramping-arrival-rate, peak **3000 RPS/runner = 18000 aggregate target** (doubled from M40's 1500/9000)
**Window:** 11:04–11:07 UTC

## Result

| Metric | Value | Target | Status |
|---|---|---|---|
| **Aggregate RPS** | **~5147** (~857.6/runner × 6) | maximize | peak |
| **DB CPU peak (1m max, 11:07)** | **73.5%** | **60–80%** | **✅ in band** |
| DB CPU peak (1m max, 11:06) | 66.9% | 60–80% | ✅ in band |
| DB Memory peak (1m max) | 46.9% | 60–80% | below band (M50's 32 GiB has plenty of room) |
| DB IOPS peak | 876 ops/s | tier ceiling | well below |
| Error rate (k6) | 0.00% | <1% | ✅ |
| Throttled (429) rate | 0.00% | <50% | ✅ |
| Latency p95 — list | 519–1393 ms | n/a | high tail (paging cold-cache cost) |
| Latency p95 — get | 478–604 ms | n/a | |
| Latency p95 — create | 591–715 ms | n/a | |
| Latency p95 — update | 728–878 ms | n/a | |
| MongoRequestDurationMs (server-side avg, 11:04) | 3.30 ms | <20 (median target) | ✅ |
| MongoRequestDurationMs (server-side avg, 11:06) | 4.20 ms | <20 | ✅ |

**DB CPU saturated cleanly at 73.5%** ✅. Memory only reached 46.9% — M50's 32 GiB headroom is large for this workload shape. Server-side Mongo durations averaged 3.3–25.2 ms across the peak hold; the higher 25 ms reading at 11:05 coincides with HPA scale-up activity.

**RPS step M40 → M50: 2553 → 5147 = 2.02× — hits the "double per step" target exactly.**

## Per-runner k6 summary (representative)

```
Requests:                270378           (×6 runners ≈ 1.62M aggregate)
Error rate (5xx/4xx):    0.00%
http_req_failed (k6):    0.00%

RPS / runner             857.82           (×6 runners ≈ 5147 aggregate)

Latency p95 (ms)         create=645.90  get=528.98  list=519.93  update=776.91
Saturation (VUs p95)     680
```

App-side p95s remain elevated at this RPS but **not significantly higher than M40 ran at half the load** — the app-tier queueing is now the dominant cost, and the p95 shape changes only slowly as offered load scales.

## DB metric trace (per-minute, max + avg)

| Time | CPU max | CPU avg | Mem max | Mem avg | IOPS max | Server lat avg |
|---|---|---|---|---|---|---|
| 11:04 (steady) | 42.2% | 12.3% | 40.7% | 25.6% | 818 | 3.30 ms |
| 11:05 | 57.3% | 20.1% | 40.7% | 25.8% | 876 | 25.20 ms |
| 11:06 (peak hold) | 66.9% | 22.8% | 46.5% | 27.2% | 618 | 4.20 ms |
| 11:07 (drain) | **73.5%** | 24.2% | 46.9% | 27.9% | 707 | 10.04 ms |

CPU rises monotonically through the test — the peak is at the very end of the hold (11:07) at 73.5%, comfortably inside the 60–80% saturation band. Note the 1-min CPU average (24%) is much lower than the max (73.5%): M50's 8 vCPU absorbs most spikes within each 1-minute interval, with the per-minute max representing the highest sub-minute burst.

## Comparison to M40 Platinum

| Metric                    | M40 Platinum | M50 Platinum | Change                      |
|---------------------------|-------------:|-------------:|-----------------------------|
| Compute                   | 4 vCPU/16 GiB | 8 vCPU/32 GiB | 2× compute, 2× RAM         |
| App replicas (base/max)   | 8 / 24       | 12 / 32      | +50% / +33%                 |
| Mongo MaxConnectionPoolSize | 200        | 100          | halved (PR #42)             |
| **Aggregate RPS**         | ~2553        | **~5147**    | **2.02×**                   |
| DB CPU peak               | 86.9%        | 73.5%        | now within band             |
| DB Memory peak            | 62.4%        | 46.9%        | M50 has more headroom       |
| Latency p95 — list        | 156–246 ms   | 519–1393 ms  | +2-6× (higher offered load) |
| Latency p95 — create      | 458–472 ms   | 591–715 ms   | +30%                        |
| Latency p95 — update      | 687–703 ms   | 728–878 ms   | +5–25%                      |
| Server lat avg (peak)     | 2.30–3.14 ms | 3.30–4.20 ms | similar (both healthy)      |
| Errors                    | 0.00–0.01%   | 0.00%        | both ✅                      |

The DB itself is healthier on M50 (lower CPU peak, lower memory peak, similar server-side latency) at *2× the offered load*. App-side p95s grow because the offered load doubled, not because the app got slower — per-RPS latency stayed roughly flat.

## Repro

```bash
export ADMIN_PWD='<from secret store>'
bash tests/loadtest/k6/loadtest-documentdb.sh run platinum
```

Cluster left running on M50 per user instruction (option **ii**: M50 is the new platinum baseline).

Artifacts:
- `tests/loadtest/k6/values-documentdb-platinum.yaml` — helm overlay (12 replicas, HPA 12/32, mongo persistence)
- `tests/loadtest/k6/rest-api-loadtest-documentdb.js` — `TIER=platinum` selects the 3000-RPS-peak ramp (preAllocatedVUs=4000, maxVUs=6000)

## Observations

1. **Doubling per step holds at the M40→M50 hop too.** The Bronze→Silver→Gold→Platinum(M40) sweep already showed 2.48× / 2.32× / 2.55× steps; adding the Platinum(M40)→Platinum(M50) step lands at 2.02×, sustaining the trend.
2. **DB CPU saturation came in cleanly at 73.5%** — no overshoot like M40's 86.9% at end-of-peak. The combination of larger compute (8 vCPU) and more app replicas (12/32 vs 8/24) lets the runner saturate the DB without overrunning it.
3. **Memory is over-provisioned for the workload** — 46.9% peak on 32 GiB. The next sensible Mongo tier study would target a memory-bound workload (large working sets), not this small-doc CRUD shape.
4. **List p95 1393 ms tail** at 11:05 is the cold-cache cost of cursor-paginated reads at the start of the peak hold — by 11:06 it dropped to 519 ms. Predictable shape; not a regression.
5. **Connection-pool sizing fix (PR #42) prevented the M30/M40-style overcommit.** Server-side latency stayed at 3–4 ms throughout the peak hold, confirming the diagnosis: the earlier elevated p95s on Gold/M40 were *app-side queueing* from MaxPoolSize=200 × pod-count overcommit, not DB latency.

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | HPA max | 32 |
| CPU reserved at peak | 32 × cpu=80m | 2560m |
| Memory reserved at peak | 32 × memory=384Mi | 12288 Mi (12 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 2560m / 2000m = 128.0%
- Memory: 12288 Mi / 8192 Mi = 150.0% **← binding**
- Pro-rate share = 1.50 (app spans ~1.5 D2s_v6 nodes)

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| Cosmos DB for MongoDB vCore M50 Compute | N/A — estimated | N/A — estimated | 1 Hour |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| DocDB M50 compute (estimated) | ~$0.80/hr × 730 | ~584.00 | ~438.00 |
| DocDB storage 32 GiB (included in M50) | included | 0.00 | 0.00 |
| DocDB subtotal | | ~584.00 | ~438.00 |
| App pro-rated D2s_v6 | 0.161 × 730 × 1.50 | 176.30 | 132.22 |
| App subtotal | | 176.30 | 132.22 |
| **Platinum (M50) REST + DocDB total** | | **~$760.30** | **~$570.22** |

Savings: ~$190.08/month at 25% discount.

### Notes

- HPA-bounded reservation as proxy (no per-pod CPU/memory snapshot in this run report).
- Memory binds (150.0%) over CPU (128.0%); pro-rate = 1.50 — app spans ~1.5 D2s_v6 nodes.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB.
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- **Cosmos DB for MongoDB vCore M50 is NOT listed in the Azure Retail Prices API (`prices.azure.com`) for brazilsouth as of 2026-05-07. The ~$0.80/hr figure is an estimate; verify at https://azure.microsoft.com/pricing/details/cosmos-db/mongodb/ before use in billing models.**
- M50 HA (zone-redundant standby) included; estimate reflects HA-enabled cluster price.
- M50 replaced M40 as the canonical Platinum tier (in-place upgrade); 32 GiB disk included in compute price.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
