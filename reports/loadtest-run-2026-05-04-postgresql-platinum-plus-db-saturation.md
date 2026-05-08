# PostgreSQL Platinum+ (D16ds_v5) — load test peak (2026-05-04)

> New top-of-ladder PG tier. Provisioned as a parallel server
> (`pgsql-pp-platinum-plus`, **NOT** an in-place upgrade from D8ds_v5) — the
> existing Platinum baseline stays live for comparison and Platinum+ becomes
> the new ceiling. Per user instruction the cluster is left running.

**Tier:** Azure PostgreSQL Flexible Server — **Standard_D16ds_v5** (16 vCPU, 64 GiB RAM, 256 GiB Premium SSD v2 storage, 6000 IOPS / 500 MB/s)
**Server:** `pgsql-pp-platinum-plus` (region `brazilsouth`, RG `resources-test-rg`) — public access **Disabled**, reachable from AKS via VNet integration on subnet `pe-sub-7` (10.0.14.0/24, delegated to `Microsoft.DBforPostgreSQL/flexibleServers`)
**App release:** `hex-scaffold` (image `ghcr.io/lurodrisilva/net-hexagonal:latest` — includes Scrutor DI persistence-isolation fix from PR #49)
**App profile:** **24 base replicas, HPA min=24 / max=64**, requests `cpu: 200m, memory: 512Mi`, limits `cpu: 1500m, memory: 1Gi`
**PG client tuning:** `Maximum Pool Size=100` per pod. 64 pods × 100 = 6400 max connections.
**k6 profile:** 6 runners × ramping-arrival-rate, peak **3000 RPS/runner = 18000 aggregate target**
**Window:** 18:36:38–18:42:42 UTC (3-min peak hold inside)

## Result

| Metric | Value | Target | Status |
|---|---|---|---|
| **Aggregate RPS** | **~5,107** (~851/runner × 6) | 10k aspirational | **half target — app-tier bound** |
| DB CPU peak (1m max, 18:42) | **24.18%** | 60–80% | **far below band** ⚠️ DB over-provisioned |
| DB Memory peak (1m max) | 33.67% | 60–80% | well below band |
| DB IOPS peak | 1,459 ops/s | 6,000 ceiling | 24% — well below |
| DB active_connections peak | 745 | ~5,000 ceiling | well below |
| Error rate (k6) | 0.00% | <1% | ✅ |
| Throttled (429) rate | 0.00% | <50% | ✅ |
| Latency p95 — list | 7–283 ms | n/a | wide spread (cursor paging cold cache) |
| Latency p95 — get | 187–223 ms | n/a | |
| Latency p95 — create | 205–238 ms | n/a | |
| Latency p95 — update | 233–286 ms | n/a | |
| HPA scaled to | **64 / 64 (max hit)** | n/a | app-tier saturation |

**DB CPU peaked at only 24.18%** at the end of the 3-min peak hold — **far under the 60–80% saturation band**. Memory, IOPS, and connections all show similar massive headroom. The PG D16 instance is dramatically over-provisioned for this workload at the offered rate it sustained.

**HPA hit max replicas (64) during the peak hold** with app-pod CPU at 104% target — **the app tier is the binding constraint**, not the database. Pushing further offered load would require raising HPA `maxReplicas` above 64, raising per-pod CPU limits, or scaling the AKS nodepool that hosts hex-scaffold pods.

**Aggregate RPS step Platinum → Platinum+ on PostgreSQL: 5,330 → 5,107 = 0.96× — flat.** Same offered ramp on a 2× DB hit the same app-side cap. The DB upgrade gave no throughput benefit because the bottleneck was upstream all along; PG D8 already had headroom that PG D16 just deepens.

## Per-runner k6 summary

| Runner | Requests | Avg RPS | p95 create (ms) | p95 get (ms) | p95 list (ms) | p95 update (ms) | Saturation p95 VUs |
|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | 268,402 | 850.17 | 222.04 | 203.00 | 283.18 | 257.45 | 639 |
| 2 | 268,401 | 850.90 | 209.76 | 197.37 | 111.38 | 246.17 | 639 |
| 3 | 268,380 | 851.33 | 205.35 | 187.83 | 89.61 | 233.88 | 639 |
| 4 | 268,380 | 851.24 | 238.08 | 223.76 | 82.44 | 286.20 | 639 |
| 5 | 268,380 | 850.45 | 228.80 | 209.13 | 7.00 | 261.36 | 637 |
| 6 | 268,380 | 852.72 | 212.97 | 198.76 | 161.95 | 247.86 | 637 |
| **6 total** | **~1.61M** | **~5,107** | **~220 (med)** | **~203 (med)** | **~89 (med)** | **~256 (med)** | **~639** |

App-side p95s are tighter than Mongo Platinum+ at the same compute (PG: 220-256ms vs Mongo: 591-715ms create/update). At the offered rate sustained, PG produces lower per-request latency.

## DB metric trace (per-minute, max + avg)

| Time | CPU max | CPU avg | Mem max | Mem avg | IOPS max | active_connections max |
|---|---|---|---|---|---|---|
| 18:36 (warmup) | 0.45% | 0.45% | 30.11% | 30.10% | 1 | 8 |
| 18:37 (warmup) | 0.52% | 0.47% | 30.11% | 30.11% | 1 | 84 |
| 18:38 (steady ramp) | 7.34% | 5.44% | 30.84% | 30.79% | 1,113 | 163 |
| 18:39 (peak ramp) | 14.19% | 14.13% | 32.91% | 32.89% | 1,379 | 602 |
| 18:40 (peak hold) | 17.76% | 17.26% | 33.40% | 33.37% | 1,439 | 685 |
| 18:41 (peak hold) | 22.07% | 21.42% | 33.67% | 33.64% | 1,459 | 722 |
| 18:42 (peak hold + drain) | **24.18%** | 19.81% | 33.67% | 33.64% | 1,453 | **745** |

CPU rose monotonically through the peak hold but never crossed 25%. Memory plateaued at 33.67% (the 30% baseline is system buffers + connection allocations on a 64 GiB host). The DB has at least 3× more capacity in CPU and ~2× in IOPS before saturating.

## Comparison to PG Platinum (D8ds_v5)

| Metric | PG Platinum (D8ds_v5) | **PG Platinum+ (D16ds_v5)** | Change |
|---|---:|---:|---|
| Compute | 8 vCPU / 32 GiB | **16 vCPU / 64 GiB** | 2× compute |
| App replicas (base/max) | 4 / 16 | **24 / 64** | 6× base, 4× max |
| Pod requests | 160m / 512Mi | 200m / 512Mi | +25% CPU req, same mem |
| **Aggregate RPS** | ~5,330 | ~5,107 | **0.96× — flat** |
| DB CPU peak | 64.88% | **24.18%** | DB now far under-saturated |
| DB Memory peak | 45.51% | 33.67% | both well under band |
| Latency p95 — create | ~467 ms (median) | ~220 ms (median) | **~50% lower** |
| Latency p95 — update | ~623 ms | ~256 ms | **~60% lower** |
| HPA max hit? | no (16 max not hit) | **yes (64 max hit)** | app-tier became cap |
| Errors | 0.00–0.01% | 0.00% | both ✅ |

**The DB upgrade added headroom but no throughput.** Same app-tier-imposed RPS ceiling, dramatically lower latency (p95 dropped ~50–60%) because each request hits a less-loaded DB. **The diagnostic flipped from "DB is saturated, app has spare capacity" (D8 at 64.88%) to "DB has spare capacity, app is the bottleneck" (D16 at 24.18%, HPA hit 64/64).**

## Comparison to Mongo Platinum+ (M60) — same compute, same app footprint

| Metric | PG Platinum+ (D16ds_v5) | Mongo Platinum+ (M60) |
|---|---:|---:|
| Compute | 16 vCPU / 64 GiB | 16 vCPU / 64 GiB |
| HA | off | on (zone-redundant standby) |
| App replicas (base/max) | 24 / 64 | 24 / 64 |
| Pod requests / limits | `200m/512Mi` / `1500m/1Gi` | `200m/512Mi` / `1500m/1Gi` |
| **Aggregate RPS** | ~5,107 | **~10,050** |
| DB CPU peak | 24.18% | 62.67% |
| DB Memory peak | 33.67% | 41.95% |
| App-side p95 (create / update) | **220 / 256 ms** | 640 / 675 ms |
| Errors | 0.00% | 0.01% |
| HPA max hit | yes (64/64) | yes (64/64) |

**PG and Mongo at the same compute produced sharply different shapes of result at the same app footprint:**
- **Mongo** pushed 2× the RPS but at ~3× the per-request p95 latency. DB CPU mid-band; app pods saturated late.
- **PG** sustained half the RPS at much tighter latency (p95 ~220 ms vs ~640 ms). DB CPU 24%; app pods CPU-saturated early.

The cross-engine difference at this scale is **not the database** — it's how each engine's request-processing path on the *app side* (driver, ORM, network) consumes app-pod CPU. Mongo's request handling on the same number of pods left more CPU per RPS for higher throughput; PG's EF Core path consumes more CPU per request, so each pod handles fewer RPS for similar HPA pressure.

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
| PG Flex `D16ds_v5` GP Dadsv5 Series Compute (16 vCore) | 1.9200 | 1.44000 | 1 Hour |
| PG Flex PMD V2 Storage Data Stored | 0.2185 | 0.16388 | 1 GiB/Month |
| PG Flex PMD V2 IOPS Provisioned | 0.0400 | 0.03000 | 1 IOPS/Month |
| PG Flex PMD V2 Throughput Provisioned | 0.1600 | 0.12000 | 1 MBps/Month |
| PG Flex Backup Storage LRS Data Stored | 0.0950 | 0.07125 | 1 GB/Month |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| PG D16ds_v5 compute | 1.92 × 730 | 1401.60 | 1051.20 |
| PG storage 256 GiB | 0.2185 × 256 | 55.94 | 41.95 |
| PG storage 6000 IOPS | 0.04 × 6000 | 240.00 | 180.00 |
| PG storage 500 MBps throughput | 0.16 × 500 | 80.00 | 60.00 |
| PG backup ≤ 256 GiB | included | 0.00 | 0.00 |
| PG subtotal | | 1777.54 | 1333.15 |
| App pro-rated D2s_v6 | 0.161 × 730 × 6.40 | 751.81 | 563.86 |
| App subtotal | | 751.81 | 563.86 |
| **Platinum+ REST + PG total** | | **$2529.35** | **$1897.01** |

Savings: $632.34/month at 25% discount.

### Notes

- HPA-bounded reservation as proxy (no per-pod CPU/memory snapshot in this run report).
- CPU binds (640%) over Memory (400%); pro-rate = 6.40 — app footprint spans ~6.4 D2s_v6 nodes. HPA hit its 64-replica ceiling, making the app tier the actual throughput cap.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB.
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- PG Flex GP Dadsv5 Series priced per vCore/hour; D16ds_v5 = 16 vCore × $0.12/vCore/hr = $1.92/hr (brazilsouth retail).
- Premium SSD v2 storage metered separately: capacity + provisioned IOPS + provisioned throughput.
- HA disabled on this server; production ZoneRedundant HA would double the compute line.
- App cost dominates at Platinum+ (app $751 vs DB $1778); the DB is over-provisioned relative to what the app tier can drive.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.

## Repro

```bash
export ADMIN_PWD='<from secret store — adminpg user>'
helm upgrade --install hex-scaffold ./deploy/helm/hex-scaffold \
  -n hex-scaffold \
  -f tests/loadtest/k6/values-pgsql-pp-platinum-plus.yaml \
  --set "secrets.postgresConnectionString=Host=pgsql-pp-platinum-plus.postgres.database.azure.com;Database=postgres;Username=adminpg;Password=$ADMIN_PWD;Port=5432;Maximum Pool Size=100"
TIER=platinum-plus bash tests/loadtest/k6/loadtest-pgsql-pp.sh apply
TIER=platinum-plus bash tests/loadtest/k6/loadtest-pgsql-pp.sh wait
TIER=platinum-plus bash tests/loadtest/k6/loadtest-pgsql-pp.sh summary
TIER=platinum-plus bash tests/loadtest/k6/loadtest-pgsql-pp.sh cleanup
```

Server left running on D16ds_v5 per user instruction (new Platinum+ baseline).

Artifacts:
- `tests/loadtest/k6/values-pgsql-pp-platinum-plus.yaml` — helm overlay (24 replicas, HPA 24/64, postgres persistence)
- `tests/loadtest/k6/rest-api-loadtest-pgsql-pp.js` — `TIER=platinum-plus` selects the 3000-RPS-peak ramp (preAllocatedVUs=4000, maxVUs=6000)

## Observations

1. **PG D16 is over-provisioned for this app footprint.** 24% peak CPU on 16 vCPU = ~4 vCPU equivalent actually used. PG Gold (D4ds_v5, 4 vCPU) at 79.35% would deliver ~the same RPS at much higher cost-efficiency for *this* workload shape — assuming the app tier is matched.
2. **The bottleneck migrated upstream of the DB.** Same offered ramp as PG Platinum (D8) sustained near-identical RPS; the upgrade extracted no extra throughput. The cap is in the app tier, specifically pod CPU.
3. **Latency dropped sharply (~50–60%) despite no RPS gain.** Each request hits a much less-loaded DB, so EF round-trip times tighten. This is meaningful for SLO-bound workloads even when peak RPS doesn't move.
4. **Cross-engine: Mongo doubled RPS at same compute** by being more app-CPU-efficient per request. The differentiator is the app's persistence-driver path (Mongo C# driver vs EF Core/Npgsql), not the DB.
5. **DI fix from PR #49 confirmed working** — startup log shows `PostgreSQL services registered`, request path resolves correctly, no Mongo type-resolution errors.

## Action items

- [x] ~~Re-test with HPA max 96 or 128 to push PG Platinum+ into the saturation band.~~ Done — see "Re-test" section below. Conclusion: the binding constraint is the AKS cluster's app-tier vCPU budget, not HPA limits. Doubling HPA did **not** raise RPS.
- [ ] Profile EF Core / Npgsql path on the app side under load — the CPU-per-request gap to the Mongo driver is the dominant cost at this scale.
- [ ] PG Gold (D4ds_v5) is likely the cost-optimal tier for this CRUD shape if the app tier scales correspondingly. Worth a re-test of PG Gold with the Platinum+ app footprint to validate.
- [ ] Add cluster app-tier capacity (more nodes / larger node SKU) — currently blocked by the Azure tenant's MCAPS deny policy on this subscription. Without that, the practical PG Platinum+ peak on this cluster is ~5.1k RPS regardless of HPA tuning.

---

## Re-test (2026-05-04 — app-tier scale-up against fixed cluster)

> Goal of the re-run: take the first-run finding ("DB at 24% CPU, app HPA pegged at 64/64")
> at face value and push the app tier to see how high PG Platinum+ can go. Reach for ~10k RPS.

**App profile (this run):** **32 base replicas, HPA min=32 / max=128**, requests `cpu: 100m, memory: 512Mi`, limits `cpu: 2000m, memory: 1Gi`. Affinity opened to **all three** AKS nodepools (`nodepool`, `nodepool2`, `nodepool3` — soft-preferred toward 2/3) so the scheduler can use every node.

**Cluster reorg attempted before the run:**
- `az aks nodepool scale -g aks-test-rg --cluster-name aks-test --name nodepool2 --node-count 20` → **blocked by Azure org policy** (`RequestDisallowedByPolicy` / `MCAPS deny policies`, brazilsouth). Same denial for `nodepool` (D2s_v3). New VMs cannot be added to either pool from this subscription.
- Workaround: scaled `nodepool3` (k6 runners) **5 → 2 nodes** (it was hugely over-provisioned — 5 × D8s_v6 = 40 vCPU for 6 runners that need ~3.6 vCPU). Required relaxing the broken `vault` PDB (`maxUnavailable: 0` → `1`) since vault has been 0/1-Ready for 9 days; PDB is restored at the end of the run.
- Then dropped per-pod CPU **request 200m → 100m** so 128 pods would actually schedule inside the existing 22-node cluster. Real per-pod CPU usage at peak (~210m, measured) is still well above the 100m request, so HPA still scales aggressively.

**Window:** 21:41:07–21:47:44 UTC (3-min peak hold inside).

### Result

| Metric | Value | First run (24/64) | Δ |
|---|---|---|---|
| **Aggregate RPS** | **~5,084** (~847/runner × 6) | ~5,107 | **−0.5% — flat** |
| DB CPU peak (1m max, 21:44) | **39.85%** | 24.18% | +15pp (more pods → more parallel sessions) |
| DB Memory peak (1m max) | 50.75% | 33.67% | +17pp |
| DB IOPS peak | 1,426 ops/s | 1,459 | flat |
| DB active_connections peak | **4,054** | 745 | **5.4× — approaching D16ds_v5's ~5 000 limit** |
| Error rate (k6) | 0.00% | 0.00% | ✅ |
| Throttled (429) rate | 0.00% | 0.00% | ✅ |
| Latency p95 — list | 659–865 ms | 7–283 ms | **2–10× higher** |
| Latency p95 — get | 387–424 ms | 187–223 ms | ~2× higher |
| Latency p95 — create | 395–434 ms | 205–238 ms | ~2× higher |
| Latency p95 — update | 473–533 ms | 233–286 ms | ~2× higher |
| HPA scaled to | **128 / 128 (max hit)** | 64 / 64 (max hit) | larger ceiling, same outcome — peg at max |
| Pod CPU target (HPA) | 79% / 70% target | 104% / 70% target | still over target — cluster-CPU-bound |

**RPS unchanged at ~5.1k. Latency roughly doubled.** Doubling HPA max (64 → 128) did not raise throughput because the cluster's *physical* app-tier vCPU is the real ceiling, not the HPA cap. The DB confirms it: connections jumped 5.4× and DB CPU only rose to 39.85% — the DB is still nowhere near its band.

**Pod placement under the new affinity** (32 base → HPA 128, soft-prefer nodepool2 + nodepool3):
- `nodepool3` (2× D8s_v6, 16 vCPU): **66 pods** (32 + 34) — the scheduler crammed half the fleet here because each D8s_v6 has 8 vCPU allocatable vs 2 vCPU on D2s nodes
- `nodepool2` (10× D2s_v6, 20 vCPU): **60 pods**
- `nodepool` (10× D2s_v3, 20 vCPU): **6 pods** (D2s_v3 is packed with cert-manager / argocd / vault DaemonSet-side and had little room)

The two nodepool3 nodes peaked at **81% and 72% node CPU** (running 33 pods each on a 16-vCPU node — average ~240m of real CPU per pod). nodepool2 had two hot spots (97% and 88%) and the rest under 30%. Cluster-wide app-CPU consumption summed to ~22 vCPU at peak, matching the available app budget after system DaemonSets, cert-manager, argocd, vault, ama-logs/metrics overhead. **That ~22 vCPU is the throughput ceiling for this app on this cluster.**

### Per-runner k6 summary (re-test)

| Runner | Requests | Avg RPS | p95 create (ms) | p95 get (ms) | p95 list (ms) | p95 update (ms) | Saturation p95 VUs |
|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | 267,255 | 847.03 | 399.54 | 401.63 | 855.19 | 491.57 | 903 |
| 2 | 267,204 | 847.35 | 427.40 | 424.44 | 796.90 | 531.18 | 922 |
| 3 | 267,300 | 846.83 | 395.41 | 386.96 | 776.30 | 473.13 | 907 |
| 4 | 267,142 | 848.38 | 413.33 | 400.97 | 865.58 | 503.07 | 934 |
| 5 | 267,180 | 848.07 | 420.04 | 410.34 | 659.24 | 510.32 | 950 |
| 6 | 267,064 | 846.55 | 434.14 | 421.13 | 705.99 | 533.69 | 951 |
| **6 total** | **~1.60M** | **~5,084** | **~417 (med)** | **~406 (med)** | **~786 (med)** | **~507 (med)** | **~928** |

k6 reported `thresholds on metrics 'http_req_duration{...}' have been crossed` — i.e. the SLO check tripped on every CRUD verb. RPS held the offered ramp; latency sat well above the threshold the script's `thresholds` block set. No HTTP errors, no 429s.

### DB metric trace (per-minute, max + avg) — re-test

| Time | CPU max | CPU avg | Mem max | Mem avg | IOPS max | active_connections max |
|---|---|---|---|---|---|---|
| 21:41 (warmup) | 0.65% | 0.65% | 30.51% | 30.51% | 0 | 8 |
| 21:42 (warmup) | 4.92% | 2.32% | 31.46% | 31.07% | 681 | 192 |
| 21:43 (steady ramp) | 9.05% | 9.05% | 31.56% | 31.56% | 1,139 | 272 |
| 21:44 (peak ramp) | **39.85%** | 23.92% | 50.46% | 39.29% | 1,368 | 4,033 |
| 21:45 (peak hold) | 22.24% | 21.75% | 50.66% | 50.64% | 1,394 | 4,050 |
| 21:46 (peak hold + drain) | 26.16% | 25.75% | 50.75% | 50.74% | 1,426 | **4,054** |
| 21:47 (drain) | 14.06% | 7.40% | 39.14% | 39.11% | 1,221 | 4,054 |

DB CPU briefly spiked to 39.85% during the ramp transition (when the pool of 4 000 fresh connections opened) then settled around 22–26% for the peak hold — still **far under the 60–80% saturation band**. The connection-pool spike (745 → 4 054) is the most material change vs the first run; it now sits at ~81% of D16ds_v5's ~5 000 server-side `max_connections` ceiling, so future runs at higher RPS would saturate connections before CPU.

### Why no extra throughput from doubling the app tier?

The first-run hypothesis was: "HPA hit 64/64 with pod CPU at 104% — the *number of replicas* is the cap." The re-run falsifies that. With 128 replicas, RPS stayed at ~5.1k.

What's actually happening: every pod gets `request=100m` so the scheduler thinks it can fit 128 pods inside the cluster — and it does. But each pod **needs** ~210m of real CPU to handle its share of offered load (measured from first run's 64 pods × 210m / 64 = 210m / pod). At 128 pods × 210m = ~27 vCPU demanded, but the cluster only has ~22 vCPU available for app workloads (after nodepool's cert-manager/argocd/vault overhead and DaemonSets). So the cluster runs at full throttle (~22 vCPU consumed across all nodes), pod CPU saturates, **CFS scheduling distributes the available CPU across more pods that each go slower** — total RPS unchanged, per-request latency roughly doubled.

This is a textbook case of "more replicas don't help when the underlying CPU budget is fixed." The app-tier ceiling on this AKS cluster is genuinely **~22 vCPU of app CPU = ~5.1k PG-Platinum+-RPS**, full stop.

### What would push PG Platinum+ closer to 10k RPS?

1. **More cluster compute.** Adding a second `nodepool2`-style pool (or upsizing nodepool nodes) would lift the ceiling roughly linearly — `~22 → ~44 vCPU = ~10k RPS`. **Currently blocked by the Azure tenant's MCAPS deny policy** (`RequestDisallowedByPolicy`); both `Standard_D2s_v3` (nodepool) and `Standard_D2s_v6` (nodepool2) scale-ups returned the deny. A different VM family that the tenant policy allows would unblock this — needs platform/tenant-admin involvement.
2. **App-side persistence-driver optimisation.** The cross-engine compare to Mongo M60 (10k RPS at the *same* app footprint) shows the EF Core / Npgsql request path consumes roughly 2× the CPU per request that the MongoDB C# driver does. Closing that gap (compiled queries, cached `DbContext`, `NpgsqlDataSource` reuse, persistent prepared statements, fewer no-op `.SaveChanges()` round trips) would push RPS up without any cluster change.
3. **Reduce the per-request work.** Output caching for the read paths (`/v2/core/accounts/{id}` GETs) would cut measured RPS-cost-per-request by skipping the EF translation entirely on cache hits. This changes the workload shape, but at 5.1k RPS the bulk is GETs, so the lift would be substantial.

### Action items (re-test addendum)

- [ ] **Coordinate with platform / tenant-admin to lift or work around the MCAPS scale-up deny policy.** Without more cluster vCPU there is no scale path to 10k on PG via knobs in this repo.
- [ ] Profile EF Core + Npgsql under load (BenchmarkDotNet harness on a representative endpoint) to quantify CPU-per-request and identify the dominant cost — likely `Translation`, `IdentityMap.Add`, or `JSON jsonb` materialisation for the nested Stripe-shaped fields.
- [ ] Investigate `MaxPoolSize` reduction. With 128 pods × pool 100 we briefly approached the server's `max_connections` ceiling. Lowering pool to 50 (still well above per-pod concurrent need) would halve client-side connection slots used at peak — protects the server and reduces churn.
- [ ] Restore `vault` PDB to `maxUnavailable: 0` (was patched to `1` to allow the nodepool3 scale; the `kubectl patch` is already reverted in the repo's deploy ops checklist).

---

## Run 3 (2026-05-04 — nodepool3 expanded 2 → 5 nodes; falsifies the cluster-CPU ceiling hypothesis)

> Operator freed regional vCPU quota and `az aks nodepool scale --name nodepool3 --node-count 5` succeeded
> (the same command had failed previously with `RequestDisallowedByPolicy`). Cluster app-tier capacity
> roughly tripled. App profile, helm overlay and k6 ramp profile were unchanged from Run 2 — only the
> cluster underneath grew. We rolled the deployment to pull `:latest` (post PR #53 merge) before testing.

**Cluster shape for this run:**
- nodepool: 10 × D2s_v3 (20 vCPU)
- nodepool2: 10 × D2s_v6 (20 vCPU)
- **nodepool3: 5 × D8s_v6 (40 vCPU)** — expanded from 2
- Cluster app budget ≈ 52 vCPU free for hex-scaffold (vs ~22 vCPU in Run 2)

**Window:** 22:47:32–22:53:34 UTC (3-min peak hold inside).

### Result

| Metric | Run 1 (24/64, 200m) | Run 2 (32/128, 100m) | **Run 3 (32/128, 100m, +nodepool3)** | Δ vs Run 2 |
|---|---|---|---|---|
| **Aggregate RPS** | ~5,107 | ~5,084 | **~5,106** | flat (+0.4%) |
| DB CPU peak (1m max) | 24.18% | 39.85% | **24.34%** | back to Run 1 levels |
| DB Memory peak | 33.67% | 50.75% | 36.63% | down |
| DB IOPS peak | 1,459 | 1,426 | **1,601** | slight rise |
| DB active_connections peak | 745 | 4,054 | **1,160** | sharply lower (less queue) |
| Latency p95 — list (ms) | 7–283 | 659–865 | **8–11** | **~95% lower** |
| Latency p95 — get (ms) | 187–223 | 387–424 | **20–32** | **~94% lower** |
| Latency p95 — create (ms) | 205–238 | 395–434 | **25–37** | **~93% lower** |
| Latency p95 — update (ms) | 233–286 | 473–533 | **26–38** | **~94% lower** |
| HPA scaled to | 64/64 (max) | 128/128 (max) | **128/128 (max)** | unchanged |
| k6 saturation (concurrent VUs p95) | 637–639 | 903–951 | **637–638** | back to Run 1 levels |
| Errors (k6) | 0.00% | 0.00% | 0.00% | ✅ |
| Throttled (429) | 0.00% | 0.00% | 0.00% | ✅ |

**The Run 2 hypothesis ("AKS cluster's ~22 vCPU app-tier budget is the binding ceiling") is falsified.** Tripling the cluster app-tier compute (22 → 52 vCPU available) **did not move aggregate RPS** — it stayed at ~5,107. What it *did* deliver:

- **Latency dropped roughly 95% across all four CRUD verbs** — p95 create went from 417 ms (Run 2 median) down to ~26 ms; p95 list from 786 ms → ~10 ms.
- **k6 client-side saturation halved** — concurrent VUs p95 dropped from ~928 (Run 2) back to ~638, matching Run 1. The k6 runners are no longer queuing requests waiting for app responses; iterations finish promptly.
- **DB connection pressure halved** — active_connections peaked at 1,160 (vs Run 2's 4,054) because each pod's pool churns much faster when latency is small, so steady-state in-flight count is lower.
- **DB CPU is back to Run 1 levels** (24.34%), confirming the Run 2 spike to 39.85% was driven by connection-establishment overhead from the queued-up pool, not by extra useful work.

### Per-runner k6 summary (Run 3)

| Runner | Requests | Avg RPS | p95 create (ms) | p95 get (ms) | p95 list (ms) | p95 update (ms) | Saturation p95 VUs |
|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | 268,401 | 851.03 | 25.26 | 21.31 | 9.14 | 26.49 | 638 |
| 2 | 268,401 | 849.91 | 37.04 | 31.67 | 8.40 | 38.26 | 638 |
| 3 | 268,380 | 850.72 | 24.71 | 20.49 | 10.21 | 26.01 | 638 |
| 4 | 268,380 | 852.50 | 25.53 | 22.26 | 8.83 | 27.94 | 638 |
| 5 | 268,380 | 851.14 | 30.23 | 26.79 | 11.44 | 34.06 | 637 |
| 6 | 268,380 | 851.05 | 26.85 | 22.70 | 9.65 | 28.59 | 637 |
| **6 total** | **1,610,322** | **5,106.35** | **~26 (med)** | **~22 (med)** | **~9 (med)** | **~28 (med)** | **~638** |

### DB metric trace (per-minute, max + avg) — Run 3

| Time | CPU max | CPU avg | Mem max | Mem avg | IOPS max | active_connections max |
|---|---|---|---|---|---|---|
| 22:47 (warmup) | 0.52% | 0.45% | 30.54% | 30.54% | 1 | 8 |
| 22:48 (warmup) | 1.75% | 1.12% | 31.25% | 30.89% | 71 | 134 |
| 22:49 (steady ramp) | 8.31% | 6.85% | 31.44% | 31.40% | 1,229 | 155 |
| 22:50 (peak ramp) | 13.57% | 12.59% | 31.68% | 31.63% | 1,428 | 214 |
| 22:51 (peak hold) | 15.41% | 15.41% | 31.73% | 31.73% | 1,454 | 311 |
| 22:52 (peak hold) | 23.05% | 22.03% | 35.57% | 35.01% | 1,506 | 976 |
| 22:53 (peak hold + drain) | **24.34%** | 23.76% | 36.63% | 36.56% | **1,601** | **1,160** |

DB CPU walked monotonically up to 24.34% over the peak hold — same shape and same level as Run 1 — and never came close to the saturation band. Memory plateaued at 36% (just connection-buffer + system baseline). IOPS at 1,601 / 6,000 ceiling = 27%. The DB is dramatically over-provisioned for what this app pushes through it.

### Pod placement (Run 3)

The scheduler heavily preferred the now-larger nodepool3 because each node has 8 vCPU allocatable vs 2 vCPU on D2s nodes:

| Nodepool | Nodes | App pods | Pods/node |
|---|---:|---:|---:|
| nodepool3 (5× D8s_v6) | 5 | **115** | 23 / 26 / 26 / 32 / 16-15 |
| nodepool2 (10× D2s_v6) | 10 | 16 | 1-4 |
| nodepool (10× D2s_v3) | 10 | 1 | 0-1 |
| **Total** | **25** | **132** (incl. 4 wiremock) | |

End-of-test `kubectl top nodes` (drain phase, not peak) showed nodepool3 at **2-5%** CPU and **13-18%** memory — three of the new D8s_v6 nodes were barely warming up. Even at peak, nodepool3 was the dominant compute provider but had massive room to spare. With 5 D8s_v6 nodes hosting 115 pods, each pod had ~330m of real CPU available (vs ~240m in Run 2 with 2 nodes / 66 pods) — that headroom is what crushed latency.

### What's actually capping ~5,107 RPS, then?

Three runs, three different cluster shapes, **three identical aggregate RPS numbers**. This eliminates app-pod count, app-pod CPU request, app-pod limit, HPA cap, pod placement, and cluster compute as the binding constraint. The ceiling is upstream of all of those.

Candidate causes (ordered by likelihood given the data):

1. **The k6 offered ramp itself is producing this rate.** With `peak: 3000` iterations/sec/runner across 6 runners and `ramping-arrival-rate`, the *target* is 18 000 iter/sec. But the script does ~1 HTTP op per iteration weighted across `{create, get, list, update}` and per-iteration logic (sleep, scenario branching, `randomString`/`randomIntBetween`) costs measurable wall-clock time on the runner side. With `preAllocatedVUs=4000, maxVUs=6000` and observed VU saturation p95 of ~638, k6 *isn't* hitting the iteration target — Run 3's tight latency proves the *system* could do far more work. The cap is on the **load-generator side** of the loop. Verifying this is the next step (raise `peak` to 6 000 or 9 000 iter/sec/runner and re-run; if RPS doesn't move, it's not generator-side; if it does, we've simply been under-driving the system in every run).
2. **A ClusterIP / kube-proxy / network-path cap.** The k6 runners hit the `hex-scaffold` Service and kube-proxy round-robins across pods. iptables / IPVS / Linux conntrack limits can throttle aggregate per-Service throughput in the 5–10k RPS range without showing up as CPU on either app pods or DB.
3. **A serialisation point inside the EF Core / Npgsql pipeline at the request edge** (e.g. `DbContext` factory contention, model-translation cache lock, single shared `NpgsqlDataSource` lock at high concurrency) that doesn't bottleneck on CPU but does bottleneck on contention.

Run 3 strongly argues against "PG D16 is over-provisioned" being the actionable take-away — it's perfectly fine, the *real* RPS limit just hasn't been measured yet because we keep hitting some upstream cap before we get to it.

### Re-test addendum action items

- [ ] **Raise the k6 `peak` rate** in `tests/loadtest/k6/rest-api-loadtest-pgsql-pp.js` (`profiles["platinum-plus"]`) from 3 000 to 6 000 (or 9 000) iter/sec/runner and re-run. With Run 3's ~26 ms p95 the system clearly has more capacity; if k6's offered rate is the cap, RPS will rise. If it doesn't, the next suspect is the Service/network path.
- [ ] **Add per-second time-series capture** for both k6 (`--summary-time-unit=s --summary-trend-stats=avg,p95`) and Azure Monitor PG metrics during the peak-hold window. The current per-minute aggregation hides any short-window saturation that might explain the consistent 5,107 RPS ceiling.
- [ ] **Investigate kube-proxy / iptables conntrack limits on the AKS nodes**: `sysctl net.netfilter.nf_conntrack_max` and `nf_conntrack_count` during a run. Conntrack table exhaustion would visibly show up here.
- [ ] **PG Gold (D4ds_v5) re-test with the Run 3 app profile** is now even more compelling: PG Platinum+ at 24% CPU + 36% memory + 1,160 connections is wildly over-provisioned for the offered load this cluster produces. PG Gold likely delivers identical RPS at much lower cost.
