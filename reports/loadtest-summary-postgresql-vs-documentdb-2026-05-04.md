# Five-Tier Load Test Summary — PostgreSQL vs DocumentDB (Cosmos for MongoDB vCore)

> **Naming note:** The Mongo M40 run, originally labelled "Platinum (M40)", has been **renamed to Gold tier** so that compute-equivalent tiers align across engines (Mongo Gold = M40 = 4 vCPU / 16 GiB ≈ Postgres Gold D4ds_v5). New Mongo Platinum is M50. The previous M30 run is retained in the historical sweep but is no longer the canonical Gold.

All runs target the **60–80% saturation band** on the limiting resource. Region: `brazilsouth`. App: `hex-scaffold` on AKS. Load source: 6 k6 runners × ramping-arrival-rate.

## PostgreSQL Flexible Server

| Tier | SKU | Compute | Aggregate RPS | Limiting metric | Saturation | In band? | Detail |
|------|-----|---------|--------------:|-----------------|-----------:|----------|--------|
| Bronze | B1ms (Burstable) | 1 vCPU / 2 GiB | **~138** | memory_percent | 76.89% | ✅ | [report](./loadtest-run-2026-05-04-postgresql-bronze-db-saturation.md) |
| Silver | D2ds_v5 | 2 vCPU / 8 GiB | **~473** | cpu_percent | 47.79% | ⚠️ under (CPU never crossed 60%) | [report](./loadtest-run-2026-05-04-postgresql-silver-db-saturation.md) |
| Gold | D4ds_v5 | 4 vCPU / 16 GiB | **~2,616** | cpu_percent | 79.35% | ✅ | [report](./loadtest-run-2026-05-04-postgresql-gold-db-saturation.md) |
| Platinum | D8ds_v5 | 8 vCPU / 32 GiB | **~5,330** | cpu_percent | 64.88% | ✅ | [report](./loadtest-run-2026-05-03-postgresql-platinum-db-saturation.md) |
| Platinum+ | D16ds_v5 | 16 vCPU / 64 GiB | **~5,107 → ~5,084 → ~5,106** (3 runs) | cpu_percent | 24.18% → 39.85% → 24.34% | ⚠️ far under (DB over-provisioned). **3 runs at 3 cluster shapes all yield ~5.1k RPS** — ceiling is upstream of cluster, DB, HPA. Run 3 (nodepool3 expanded) cut p95 latency ~95% with no RPS gain. | [report](./loadtest-run-2026-05-04-postgresql-platinum-plus-db-saturation.md) |

## DocumentDB (Cosmos for MongoDB vCore) — canonical ladder

| Tier | SKU | Compute | HA | Aggregate RPS | Limiting metric | Saturation | In band? | Detail |
|------|-----|---------|----|---------------:|-----------------|-----------:|----------|--------|
| Bronze | M10 | 0.5 vCPU / 2 GiB | off | **~174** | memory_percent | 68.9% | ✅ (CPU 53.6% just below) | [report](./loadtest-run-2026-05-04-documentdb-bronze.md) |
| Silver | M20 | 1 vCPU / 4 GiB | off | **~432** | cpu + memory | 78.8% / 61.1% | ✅ both | [report](./loadtest-run-2026-05-04-documentdb-silver.md) |
| Gold (M40) | M40 | 4 vCPU / 16 GiB | on | **~2,553** | cpu_percent | 86.9% | ⚠️ overshoot | [report](./loadtest-run-2026-05-04-documentdb-gold-m40.md) |
| Platinum (M50) | M50 | 8 vCPU / 32 GiB | on | **~5,147** | cpu_percent | 73.5% | ✅ | [report](./loadtest-run-2026-05-04-documentdb-platinum-m50.md) |
| **Platinum+ (M60)** | M60 | **16 vCPU / 64 GiB** | on | **~10,050** | cpu_percent | **62.67%** | ✅ (HPA hit max — bottleneck migrated DB → app) | [report](./loadtest-run-2026-05-04-documentdb-platinum-plus.md) |

Historical (no longer canonical): M30 — 2 vCPU / 8 GiB, HA on, ~1,003 RPS, CPU 76.96% ✅. Filed under [`loadtest-run-2026-05-04-documentdb-gold.md`](./loadtest-run-2026-05-04-documentdb-gold.md) for the original sweep.

## App Profile per Tier and Run

The hex-scaffold app pod is the same image across every run; what changes per tier is the replica count, HPA bounds, and (for PG Platinum only) request sizing. HPA target is **70% CPU / 75% memory** on every run.

### PostgreSQL runs

| Tier | Replicas (HPA min/max) | Pod requests | Pod limits | Notes |
|------|-----------------------:|--------------|------------|-------|
| Bronze   | **4 / 8**   | `cpu=80m, mem=384Mi`  | `cpu=1000m, mem=768Mi` | Baseline app footprint for B1ms tier |
| Silver   | **6 / 12**  | `cpu=80m, mem=384Mi`  | `cpu=1000m, mem=768Mi` | App tier never CPU-bound at this run |
| Gold     | **8 / 16**  | `cpu=80m, mem=384Mi`  | `cpu=1000m, mem=768Mi` | Doubled replicas vs Bronze |
| Platinum | **4 / 16**  | `cpu=160m, mem=512Mi` | `cpu=1000m, mem=768Mi` | **Higher requests** to reduce HPA churn at peak; lower base replicas because each pod is more capacity-rich |
| Platinum+ (Run 1) | **24 / 64** | `cpu=200m, mem=512Mi` | `cpu=1500m, mem=1Gi` | HPA hit max 64/64 with pod CPU at 104% — app tier became the cap before DB even reached the band. |
| Platinum+ (Run 2 — app-tier scale-up) | **32 / 128** | `cpu=100m, mem=512Mi` | `cpu=2000m, mem=1Gi` | All-nodepool affinity. HPA hit 128/128 → **same ~5.1k RPS**, latency ~2× higher. Doubling pods squeezed real CPU per pod (240m). Initial diagnosis: cluster app vCPU budget is the cap. |
| **Platinum+ (Run 3 — nodepool3 expanded 2→5)** | **32 / 128** | `cpu=100m, mem=512Mi` | `cpu=2000m, mem=1Gi` | Same overlay as Run 2. Cluster app budget tripled (22 → 52 vCPU). HPA hit 128/128. **RPS unchanged at ~5,106** but **p95 latency ~95% lower** (create 26 ms median; list 9 ms). Cluster CPU was *not* the binding constraint after all — ceiling is upstream (k6 offered rate, network path, or app-side serialisation). |

### DocumentDB (Mongo vCore) runs

| Tier | Replicas (HPA min/max) | Pod requests | Pod limits | Notes |
|------|-----------------------:|--------------|------------|-------|
| Bronze (M10)        | **4 / 8**   | `cpu=80m, mem=384Mi` | `cpu=1000m, mem=768Mi` | App-tier capped well below offered load |
| Silver (M20)        | **6 / 12**  | `cpu=80m, mem=384Mi` | `cpu=1000m, mem=768Mi` | DB-tier-bound (both CPU + memory in band) |
| Gold M30 (historical) | **8 / 16** | `cpu=80m, mem=384Mi` | `cpu=1000m, mem=768Mi` | Original sweep — no longer canonical |
| Gold (M40)          | **8 / 24**  | `cpu=80m, mem=384Mi` | `cpu=1000m, mem=768Mi` | HPA cap raised to 24 to push past M30's RPS |
| Platinum (M50)      | **12 / 32** | `cpu=80m, mem=384Mi` | `cpu=1000m, mem=768Mi` | **Bumped from 8/24** — more replicas needed to saturate M50's 8 vCPU; pairs with `MaxConnectionPoolSize=100` (PR #42) |
| **Platinum+ (M60)** | **24 / 64** | `cpu=200m, mem=512Mi` | `cpu=1500m, mem=1Gi` | **Doubled from M50** in proportion to doubled DB compute. Heavier per-pod requests + larger limits keep p95 tight at sustained 10k RPS. HPA hit max (64) during the run — app tier became the cap. |

Cross-engine note at matched tiers: PG and Mongo runs use **the same replica counts and HPA bounds at Bronze, Silver, and the historical Gold (M30)**. They diverge at Gold/Platinum where Mongo runs scaled out more aggressively (24 max → 32 max) to push the higher-tier DBs into the saturation band, while PG Platinum took the opposite tack — fewer base replicas but heavier per-pod requests.

## Cross-Engine Comparison at the Same Tier Name

| Tier (compute) | Postgres SKU | PG RPS | Mongo SKU | Mongo RPS | Δ |
|---|---|---:|---|---:|---|
| Bronze (~1 vCPU / 2 GiB) | B1ms burstable | ~138 | M10 (0.5 vCPU) | ~174 | Mongo +26% (PG credit-throttled) |
| Silver (2 vCPU / ~8 GiB) | D2ds_v5 (2 / 8) | ~473 | M20 (1 / 4) | ~432 | **PG +9.5%** despite 2× vCPU advantage |
| Gold (4 vCPU / 16 GiB) | D4ds_v5 | ~2,616 | M40 | ~2,553 | ≈ parity (PG +2.5%) |
| Platinum (8 vCPU / 32 GiB) | D8ds_v5 | ~5,330 | M50 | ~5,147 | ≈ parity (PG +3.6%) |
| **Platinum+ (16 vCPU / 64 GiB)** | D16ds_v5 | **~5,107 → ~5,084 → ~5,106** (Run 1/2/3) | M60 | **~10,050** | **Mongo +97%** at the offered ramp. PG sits at a hard ~5,107 RPS ceiling across 3 runs spanning 22 → 52 vCPU of cluster compute. Run 3 (nodepool3 2→5) confirmed the cap is **not** cluster CPU, not HPA, not DB — ceiling is upstream (k6 offered rate / network path / app serialisation). Run 3 dropped PG p95 latency by ~95% with no RPS gain. |

## Deep Dives by Tier

Each tier is compared apples-to-apples between engines (DB tier + app footprint + saturation + latency). Numbers are pulled directly from the linked detail reports.

### Bronze-Tier Deep Dive — PG Bronze vs Mongo Bronze

| Metric | Postgres Bronze (B1ms) | Mongo Bronze (M10) |
|---|---|---|
| Compute | 1 vCPU / 2 GiB (burstable) | 0.5 vCPU / 2 GiB |
| HA | none (single node) | off |
| App replicas (HPA min/max) | 4 / 8 | 4 / 8 |
| Pod requests / limits | `80m/384Mi` / `1000m/768Mi` | `80m/384Mi` / `1000m/768Mi` |
| Aggregate RPS | ~138 | **~174** |
| DB CPU peak | 93.48% max (credit-exhaust drain artifact); 32.31% avg | 53.6% (just below band) |
| DB Memory peak | **76.89%** (✅ band) | **68.9%** (✅ band) |
| App-side p95 (create / update) | ~138 ms / similar (k6 visible only); App Insights showed 499 timeout tail | **32.47 ms / 37.42 ms** |
| Saturation verdict | Memory-bound + IOPS-throttled; burstable CPU credits cap sustained throughput | Memory-bound at 68.9%; CPU has small headroom |

**Headline:** Mongo Bronze beats PG Bronze by **+26% on RPS** while running latencies **~4× lower**. PG B1ms's burstable-CPU credit model is the dominant constraint — sustained CPU above 20% baseline depletes credits, after which throughput collapses. Beyond that, B1ms's IOPS ceiling pushed PG into seconds-level p95 with a 499-timeout tail invisible to k6. The 0.5-vCPU M10 has no comparable throttling layer. Both engines hit the memory band cleanly — this tier is genuinely memory-bound on either side.

### Silver-Tier Deep Dive — PG Silver vs Mongo Silver

| Metric | Postgres Silver (D2ds_v5) | Mongo Silver (M20) |
|---|---|---|
| Compute | **2 vCPU / 8 GiB** | 1 vCPU / 4 GiB |
| HA | off | off |
| App replicas (HPA min/max) | 6 / 12 | 6 / 12 |
| Pod requests / limits | `80m/384Mi` / `1000m/768Mi` | `80m/384Mi` / `1000m/768Mi` |
| Aggregate RPS | ~473 | ~432 |
| DB CPU peak | 47.79% (under band) | **78.8%** (✅ in band) |
| DB Memory peak | 33.63% (under band) | 61.1% (✅ in band) |
| App-side p95 (create / update) | ~14 ms / ~16 ms | ~70 ms / ~120 ms (proportional to higher tier saturation) |
| Saturation verdict | DB has 50%+ headroom — **workload-limited, not tier-limited** | **DB is the cap** |

**Headline:** PG Silver has 2× the vCPU and 2× the RAM yet only delivers ~10% more RPS than Mongo Silver. PG Silver's CPU never crossed 60% — the Postgres tier is *not the bottleneck* at this offered load. The cap is upstream (app tier or load profile), so the comparison is apples-to-oranges: Mongo Silver is genuinely tier-bound at 432 RPS while PG Silver could sustain notably more if the offered load were raised. **Action item:** PG Silver re-test with a higher offered ramp would tighten the cross-engine comparison; today's number is a lower bound on what D2ds_v5 can do.

### Gold-Tier Deep Dive — PG Gold vs Mongo Gold (M40)

| Metric | Postgres Gold (D4ds_v5) | Mongo Gold (M40) |
|---|---|---|
| Compute | **4 vCPU / 16 GiB** | **4 vCPU / 16 GiB** |
| HA | off | on (zone-redundant standby) |
| App replicas (HPA min/max) | 8 / 16 | 8 / 24 |
| Pod requests / limits | `80m/384Mi` / `1000m/768Mi` | `80m/384Mi` / `1000m/768Mi` |
| Aggregate RPS | **~2,616** | ~2,553 |
| DB CPU peak | **79.35%** (✅ in band) | 86.9% (⚠️ overshoot at 05:27, 89.65% during drain) |
| DB Memory peak | 55.77% (under band) | 62.4% (✅ in band) |
| App-side p95 (create / update) | ~298 ms / ~370 ms (one runner had list p95 outlier of 5,614 ms — connection-pool acquisition tail) | 458–472 ms / 687–703 ms |
| Server-side DB latency (avg) | n/a (single-op DB times implied <50 ms from saturation math) | 2.30–3.14 ms |
| Saturation verdict | Cleanly in band; CPU is the binding metric | Overshot the band; HA replication adds tail cost |

**Headline:** Same compute, near-identical RPS (PG +2.5%), but **PG Gold sits cleanly inside the band at 79.35% CPU while Mongo Gold M40 overshoots at 86.9%**. The HA-on configuration on Mongo (zone-redundant standby) adds replication and failover-readiness cost that PG Gold (no HA in this run) doesn't pay. PG Gold's connection-pool was close to its 1,600 ceiling (peak `active_connections=903` against 16 pods × 100 pool); Mongo Gold's pool was overcommitted (200 × 8+ pods > 1,600 cap), surfacing as the high update p95 — fixed in PR #42 by lowering pool to 100. **At the same DB compute, PG is the more comfortable choice** under this CRUD shape; closing the gap on Mongo would mean either disabling HA (changes the durability story) or stepping up to M50.

### Platinum-Tier Deep Dive — PG Platinum vs Mongo Platinum (M50)

| Metric | Postgres Platinum (D8ds_v5) | Mongo Platinum (M50) |
|---|---|---|
| Compute | **8 vCPU / 32 GiB** | **8 vCPU / 32 GiB** |
| HA | off | on (zone-redundant standby) |
| App replicas (HPA min/max) | **4 / 16** | **12 / 32** |
| Pod requests / limits | **`160m/512Mi`** / `1000m/768Mi` | `80m/384Mi` / `1000m/768Mi` |
| Aggregate RPS | **~5,330** | ~5,147 |
| DB CPU peak | 64.88% (✅ low edge of band — large headroom) | **73.5%** (✅ middle of band) |
| DB Memory peak | 45.51% (under band) | 46.9% (under band) |
| App-side p95 (create / update) | ~467 ms / ~623 ms (median across runners); list 732 ms | 591–715 ms / 728–878 ms; list 519–1393 ms (cold-cache paging) |
| Server-side DB latency (avg) | n/a (single op ~345 ms p95 implied by USL math at the knee) | 3.30–4.20 ms |
| Saturation verdict | In band but with 15+ percentage points of CPU headroom | Cleanly mid-band; HA cost absorbed |

**Headline:** Near-parity (PG +3.6%) at the practical ceiling for this CRUD workload, but the two engines reach it via very different app-tier strategies:
- **PG Platinum** uses **heavier per-pod requests** (`160m/512Mi`) with fewer base replicas (4/16). Each pod is bigger, HPA reacts less.
- **Mongo Platinum** uses **smaller pods** (`80m/384Mi`) with **more replicas** (12/32). Pool size was lowered to 100 (PR #42) to avoid overcommitting M50's connection ceiling.

PG Platinum landed at the lower edge of the band (64.88%) — it could likely push another ~30% of RPS before saturating fully. Mongo Platinum landed mid-band at 73.5% with ~26% headroom. Both engines have memory over-provisioned for this workload (~46% peak on 32 GiB) — for memory-bound workloads (large working sets) the picture would shift. **At Platinum compute, throughput is no longer the differentiator; HA posture, latency tail, and cost-per-RPS become the deciding factors.**

### Platinum+-Tier Deep Dive — PG Platinum+ (D16ds_v5) vs Mongo Platinum+ (M60)

| Metric | PG Run 1 (24/64) | PG Run 2 (32/128) | **PG Run 3 (32/128 + nodepool3 5×D8s_v6)** | Mongo Platinum+ (M60, 24/64) |
|---|---|---|---|---|
| Compute (DB) | **16 vCPU / 64 GiB** | **16 vCPU / 64 GiB** | **16 vCPU / 64 GiB** | **16 vCPU / 64 GiB** |
| HA | off | off | off | on (zone-redundant standby) |
| App replicas (HPA min/max) | **24 / 64** | **32 / 128** | **32 / 128** | **24 / 64** |
| Pod requests / limits | `200m/512Mi` / `1500m/1Gi` | `100m/512Mi` / `2000m/1Gi` | `100m/512Mi` / `2000m/1Gi` | `200m/512Mi` / `1500m/1Gi` |
| Cluster app vCPU available | ~22 | ~22 | **~52** | ~22 |
| Affinity | nodepool + nodepool2 | all 3 pools (soft pref 2/3) | all 3 pools (soft pref 2/3) | nodepool + nodepool2 |
| Aggregate RPS | **~5,107** | **~5,084** | **~5,106** | **~10,050** |
| DB CPU peak | 24.18% | 39.85% | **24.34%** | **62.67%** |
| DB Memory peak | 33.67% | 50.75% | 36.63% | 41.95% |
| DB active_connections peak | 745 | 4,054 (queue) | **1,160** | n/a |
| App-side p95 (create / update, median) | ~220 / ~256 ms | ~417 / ~507 ms | **~26 / ~28 ms** (~95% lower) | ~640 / ~675 ms |
| App-side p95 (list, median) | ~89 ms | ~786 ms | **~9 ms** | n/a |
| HPA scaled to | 64/64 (max) | 128/128 (max) | 128/128 (max) | 64/64 (max) |
| k6 saturation (concurrent VUs p95) | ~639 | ~928 | **~638** | n/a |
| Saturation verdict | App-HPA-bound (initial diagnosis) | (incorrectly) cluster-CPU-bound | **Upstream cap (k6 ramp / network / app serialisation)** — *not* DB, *not* cluster CPU, *not* HPA | App-HPA-bound; DB ~17pp headroom |

**Headline:** Three PG runs, three cluster shapes (~22 vCPU → ~22 vCPU → ~52 vCPU app capacity), **same aggregate RPS** (~5,107 / ~5,084 / ~5,106). The Mongo M60 comparison is unchanged: Mongo doubles PG's RPS at the same offered ramp, but the diagnosis of *why* moved each time the cluster did:

- **Run 1** hit HPA 64/64 with pods CPU-saturated against their request → diagnosed "HPA cap is the limit."
- **Run 2** raised HPA to 128, dropped per-pod request so 128 pods would schedule — RPS unchanged, latency doubled. Diagnosed "AKS cluster's ~22 vCPU app-tier budget is the limit."
- **Run 3** kept the same overlay but expanded `nodepool3` 2 → 5 × D8s_v6 (cluster app capacity ~22 → ~52 vCPU). RPS *still* unchanged, but **p95 latency collapsed by ~95%** across all four CRUD verbs and DB CPU returned to Run-1 levels. Earlier diagnoses falsified — the cap is **upstream of the cluster, the DB, and the HPA**: most likely the k6 offered ramp itself, the kube-proxy / ClusterIP path, or an internal serialisation point inside the EF Core / Npgsql request edge. See the linked detail report for the candidate causes and follow-up actions.

What we *did* confirm with Run 3:
- The DB at PG Platinum+ is wildly over-provisioned for what this stack pushes through it (24% CPU, 36% memory, 1.6k IOPS / 6k ceiling, 1.16k connections / 5k limit).
- More cluster CPU per pod (Run 3: ~330m real CPU / pod vs Run 2: ~240m) makes p95 latency drop near-linearly with the headroom — meaningful for SLO-bound workloads even when peak RPS doesn't move.
- The cross-engine RPS gap to Mongo (PG ~5k vs Mongo ~10k at the same compute and app footprint) **persists** even after handing PG ~2.4× the cluster CPU it had in Run 1. Mongo's gap-of-2× is being captured *upstream of cluster CPU* — likely in the offered-load path or in driver-level concurrency that PG's EF Core / Npgsql trips earlier than the Mongo driver does.

**Why couldn't we initially push past 5.1k by adding nodes?** Run 2's `nodepool` (D2s_v3) and `nodepool2` (D2s_v6) scale-up attempts returned `RequestDisallowedByPolicy` (Azure tenant MCAPS deny). Run 3 was unblocked by the operator freeing regional vCPU quota, which let `nodepool3` (D8s_v6) scale 2 → 5. nodepool / nodepool2 remain frozen at 10 nodes each — the policy applies per VMSS, and only nodepool3's was permitted. So the working pattern for app scale on this cluster is **expand nodepool3 (D8s_v6)** rather than the smaller pools.

For PG specifically: the upgrade D8 → D16 dropped p95 latency further when paired with cluster expansion (Run 3 p95 create ~26 ms vs Platinum's ~467 ms), but aggregate RPS remained flat (5,330 → 5,107 → 5,084 → 5,106). **PG Gold (D4ds_v5) is now the very obvious cost-optimal tier** for this CRUD shape — D16ds_v5 sits at 24% CPU regardless of how the app tier is shaped. Pushing past 5k RPS on PG (or finding the *real* PG ceiling) requires re-running with a higher offered k6 ramp; today's number is a property of the load generator and the network path, not of Postgres.

**RPS step ratio M50 → M60: 1.95×** — sustains the "double per step" trend on the Mongo side. On the PG side **Platinum → Platinum+ collapses to 0.96×** at the offered ramp we're using; until the upstream cap is identified and lifted, we don't have a defensible PG Platinum+ ceiling. Memory utilisation is uniformly under-provisioned across all 3 PG runs (peak 33–51%, mostly connection-pool buffers; the actual working set fits in single-digit GiB).

## Key Takeaways

- **At ≥4 vCPU the engines converge** to ~2.5–5.3k RPS for this CRUD-on-Account shape; engine choice is a wash on throughput.
- **Mongo's per-vCPU efficiency at small tiers is striking** — 1 vCPU M20 nearly matches 2 vCPU PG Silver because PG Silver was not actually saturated.
- **Workload is uniformly CPU-bound past Bronze** on both engines; memory headroom grows at every step up.
- **The relabel exposes M40 as imperfectly Gold** — its 86.9% CPU peak overshot the band; PG Gold landed cleanly at 79.35%. Apples-to-apples at the same compute, **PG Gold is more comfortably in-band than Mongo Gold**.
- **Action item:** PG Silver re-test with a higher offered ramp would tighten the cross-engine comparison; today's PG Silver number is a lower bound on what D2ds_v5 can do.
- **Platinum+ doubles the ceiling cleanly on Mongo (10k RPS at 62.67% CPU)** — the next bottleneck is *the app tier*, not the DB. Pushing past 10k requires raising HPA caps or per-pod CPU limits, or scaling the AKS nodepool that hosts hex-scaffold pods.
- **PG D16 is over-provisioned for this app footprint.** 5,107 RPS at 24.18% CPU = ~4 vCPU effectively used on a 16 vCPU server. Latency improved sharply (~50% lower than D8) but RPS stayed flat — the upgrade buys headroom and tighter latency, not throughput. PG Gold (D4ds_v5) is likely the cost-optimal tier for this CRUD shape with a matched app footprint.
- **PG Platinum+ caps at ~5,107 RPS at the current offered ramp — across 3 cluster shapes (~22 / ~22 / ~52 vCPU app capacity).** The third run (nodepool3 expanded 2 → 5 × D8s_v6) **falsified** the Run-2 diagnosis that cluster CPU was the binding constraint: tripling the available app vCPU did not move RPS, but it dropped PG's p95 latency by ~95% across all four CRUD verbs (create 26 ms median, list 9 ms). The actual ceiling is upstream of the cluster, the DB, and the HPA — most likely the k6 offered ramp itself, the kube-proxy/ClusterIP path, or an internal serialisation point in the EF Core / Npgsql request edge. **Next test:** raise the k6 `peak` rate (3000 → 6000 iter/sec/runner) — if that doesn't move RPS the cap is in the network or the app, not the load generator.
- **At Platinum+ compute the cross-engine gap to Mongo (10k vs 5k RPS) survives a 2.4× cluster CPU expansion on the PG side**, so the layer producing the gap is *upstream* of cluster CPU. Driver/ORM efficiency is still a candidate, but at PG's 24% DB CPU + ~10% pod CPU in Run 3 it can't be a CPU story alone — likely a contention/serialisation pattern in the request path that Mongo's driver doesn't exhibit. Worth profiling with end-to-end traces before another DB SKU bump.

## Monthly cost comparison (Azure Retail Prices, Brazil South, USD)

| Tier | Repo | Persistence | Pods @ peak | App share | DB subtotal | App subtotal | Total retail/mo | Total -25%/mo |
|---|---|---|---:|---:|---:|---:|---:|---:|
| Bronze | PG | B1MS / 32 GiB Std | 4–8 | 37.5% | $32.54 | $44.07 | $76.61 | $57.46 |
| Silver | PG | D2ds_v5 / 128 GiB Std SSD | 6–12 | 56.25% | $203.17 | $66.20 | $269.37 | $202.03 |
| Gold | PG | D4ds_v5 / 128 GiB PMD V2 | 8–16 | 75% | $698.37 | $88.15 | $786.52 | $589.89 |
| Platinum | PG | D8ds_v5 / 128 GiB PMD V2 | 4–16 | 128% | $1,048.77 | $150.50 | $1,199.27 | $899.45 |
| Platinum+ | PG | D16ds_v5 / 128 GiB PMD V2 | 24–128 | 640% | $1,777.54 | $751.81 | $2,529.35 | $1,897.01 |
| Bronze | DocDB | M10 / 32 GiB (est.) | 4–8 | 37.5% | ~$36.50 | $44.07 | ~$80.57 | ~$60.44 |
| Silver | DocDB | M20 / 32 GiB (est.) | 6–12 | 56.25% | ~$73.00 | $66.20 | ~$139.20 | ~$104.40 |
| Gold (M30, hist.) | DocDB | M30 / 32 GiB (est.) | 8–16 | 75% | ~$146.00 | $88.15 | ~$234.15 | ~$175.61 |
| Gold (M40) | DocDB | M40 / 32 GiB (est.) | 8–16 | 112.5% | ~$292.00 | $132.22 | ~$424.22 | ~$318.17 |
| Platinum (M50) | DocDB | M50 / 32 GiB (est.) | 4–16 | 150% | ~$584.00 | $176.30 | ~$760.30 | ~$570.22 |
| Platinum+ (M60) | DocDB | M60 / 32 GiB (est.) | 24–128 | 640% | ~$1,168.00 | $751.81 | ~$1,919.81 | ~$1,439.86 |

Methodology: persistence + app pro-rated by peak resource consumption (HPA-bounded for REST, fixed Deployment for Kafka). Binding dimension = max(CPU%, Mem%) of `Standard_D2s_v6` node capacity (2 vCPU / 8 GiB / Linux / Brazil South / $0.161/h). DocDB Mxx unit prices are estimated. 25% uniform discount; real EA/MCA/CSP agreements discount per-meter.
