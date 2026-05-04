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
| Platinum+ | D16ds_v5 | 16 vCPU / 64 GiB | **pending** ⏳ | n/a | n/a | blocked on Scrutor DI fix (#49) — cluster `pgsql-pp-platinum-plus` provisioned, idle | n/a |

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
| Platinum+ (pending) | **24 / 64** | `cpu=200m, mem=512Mi` | `cpu=1500m, mem=1Gi` | Sized to push 10k RPS once #49 lands. Helm overlay `values-pgsql-pp-platinum-plus.yaml` ready. |

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
| **Platinum+ (16 vCPU / 64 GiB)** | D16ds_v5 | **pending** ⏳ | M60 | **~10,050** | Mongo only (PG blocked on #49) — RPS step M50→M60 = **1.95×**, "double per step" trend holds |

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

### Platinum+-Tier Deep Dive — PG Platinum+ (pending) vs Mongo Platinum+ (M60)

| Metric | Postgres Platinum+ (D16ds_v5) | Mongo Platinum+ (M60) |
|---|---|---|
| Compute | **16 vCPU / 64 GiB** | **16 vCPU / 64 GiB** |
| HA | off | on (zone-redundant standby) |
| App replicas (HPA min/max) | **24 / 64** | **24 / 64** |
| Pod requests / limits | `200m/512Mi` / `1500m/1Gi` | `200m/512Mi` / `1500m/1Gi` |
| Aggregate RPS | **pending** ⏳ (blocked on #49) | **~10,050** |
| DB CPU peak | n/a | **62.67%** (✅ in 60–80% band, ~17pp headroom) |
| DB Memory peak | n/a | 41.95% (under band — 64 GiB over-provisioned) |
| App-side p95 (create / update) | n/a | ~640 ms / ~675 ms (median across 6 runners) |
| Server-side DB latency (avg) | n/a | 3.30–12.28 ms |
| HPA scaled to | n/a | **64 / 64 (max hit)** — app tier became the cap |
| Saturation verdict | n/a | DB has clear headroom; offered RPS capped by app-tier replicas, not by M60 |

**Headline:** Mongo Platinum+ (M60) **hit the 10k-RPS target with the DB still in the comfortable middle of the saturation band**. The bottleneck at this scale **migrated from the DB to the app tier** — HPA pegged at 64/64 max replicas while DB CPU was only 62.67%. Pushing further would mean raising the HPA cap (or per-pod CPU limits), not upgrading the DB.

PG Platinum+ live numbers are blocked on a pre-existing Scrutor DI bug (#49) where the inactive backend's `*Repository` class still gets registered and overrides the active backend's `IRepository<Account>` binding at request time. Mongo M60 ran cleanly because Mongo "wins" the LAST-registered race in the current Scrutor discovery order. PG D16ds_v5 hit it. The cluster is provisioned and idle; once #49 merges and CI rebuilds `latest`, the run will land in a follow-up PR with the cross-engine numbers.

**RPS step ratio M50 → M60: 1.95×** — sustains the "double per step" trend (Bronze→Silver 2.48×, Silver→Gold 2.32×, Gold→Platinum(M40) 2.55×, Platinum(M40→M50) 2.02×, Platinum→Platinum+ 1.95×). At 10k RPS the workload remains CPU-bound; memory utilization is now uniformly under-provisioned (M60: 41.95% on 64 GiB).

## Key Takeaways

- **At ≥4 vCPU the engines converge** to ~2.5–5.3k RPS for this CRUD-on-Account shape; engine choice is a wash on throughput.
- **Mongo's per-vCPU efficiency at small tiers is striking** — 1 vCPU M20 nearly matches 2 vCPU PG Silver because PG Silver was not actually saturated.
- **Workload is uniformly CPU-bound past Bronze** on both engines; memory headroom grows at every step up.
- **The relabel exposes M40 as imperfectly Gold** — its 86.9% CPU peak overshot the band; PG Gold landed cleanly at 79.35%. Apples-to-apples at the same compute, **PG Gold is more comfortably in-band than Mongo Gold**.
- **Action item:** PG Silver re-test with a higher offered ramp would tighten the cross-engine comparison; today's PG Silver number is a lower bound on what D2ds_v5 can do.
- **Platinum+ doubles the ceiling cleanly on Mongo (10k RPS at 62.67% CPU)** — the next bottleneck is *the app tier*, not the DB. Pushing past 10k requires raising HPA caps or per-pod CPU limits, or scaling the AKS nodepool that hosts hex-scaffold pods.
- **Action item:** Land #49 (Scrutor DI fix) so the deferred PG Platinum+ run can complete and the cross-engine row can be filled in.
