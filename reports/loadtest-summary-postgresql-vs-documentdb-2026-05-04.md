# Four-Tier Load Test Summary — PostgreSQL vs DocumentDB (Cosmos for MongoDB vCore)

> **Naming note:** The Mongo M40 run, originally labelled "Platinum (M40)", has been **renamed to Gold tier** so that compute-equivalent tiers align across engines (Mongo Gold = M40 = 4 vCPU / 16 GiB ≈ Postgres Gold D4ds_v5). New Mongo Platinum is M50. The previous M30 run is retained in the historical sweep but is no longer the canonical Gold.

All runs target the **60–80% saturation band** on the limiting resource. Region: `brazilsouth`. App: `hex-scaffold` on AKS. Load source: 6 k6 runners × ramping-arrival-rate.

## PostgreSQL Flexible Server

| Tier | SKU | Compute | Aggregate RPS | Limiting metric | Saturation | In band? | Detail |
|------|-----|---------|--------------:|-----------------|-----------:|----------|--------|
| Bronze | B1ms (Burstable) | 1 vCPU / 2 GiB | **~138** | memory_percent | 76.89% | ✅ | [report](./loadtest-run-2026-05-04-postgresql-bronze-db-saturation.md) |
| Silver | D2ds_v5 | 2 vCPU / 8 GiB | **~473** | cpu_percent | 47.79% | ⚠️ under (CPU never crossed 60%) | [report](./loadtest-run-2026-05-04-postgresql-silver-db-saturation.md) |
| Gold | D4ds_v5 | 4 vCPU / 16 GiB | **~2,616** | cpu_percent | 79.35% | ✅ | [report](./loadtest-run-2026-05-04-postgresql-gold-db-saturation.md) |
| Platinum | D8ds_v5 | 8 vCPU / 32 GiB | **~5,330** | cpu_percent | 64.88% | ✅ | [report](./loadtest-run-2026-05-03-postgresql-platinum-db-saturation.md) |

## DocumentDB (Cosmos for MongoDB vCore) — canonical ladder

| Tier | SKU | Compute | HA | Aggregate RPS | Limiting metric | Saturation | In band? | Detail |
|------|-----|---------|----|---------------:|-----------------|-----------:|----------|--------|
| Bronze | M10 | 0.5 vCPU / 2 GiB | off | **~174** | memory_percent | 68.9% | ✅ (CPU 53.6% just below) | [report](./loadtest-run-2026-05-04-documentdb-bronze.md) |
| Silver | M20 | 1 vCPU / 4 GiB | off | **~432** | cpu + memory | 78.8% / 61.1% | ✅ both | [report](./loadtest-run-2026-05-04-documentdb-silver.md) |
| Gold (M40) | M40 | 4 vCPU / 16 GiB | on | **~2,553** | cpu_percent | 86.9% | ⚠️ overshoot | [report](./loadtest-run-2026-05-04-documentdb-gold-m40.md) |
| Platinum (M50) | M50 | 8 vCPU / 32 GiB | on | **~5,147** | cpu_percent | 73.5% | ✅ | [report](./loadtest-run-2026-05-04-documentdb-platinum-m50.md) |

Historical (no longer canonical): M30 — 2 vCPU / 8 GiB, HA on, ~1,003 RPS, CPU 76.96% ✅. Filed under [`loadtest-run-2026-05-04-documentdb-gold.md`](./loadtest-run-2026-05-04-documentdb-gold.md) for the original sweep.

## Cross-Engine Comparison at the Same Tier Name

| Tier (compute) | Postgres SKU | PG RPS | Mongo SKU | Mongo RPS | Δ |
|---|---|---:|---|---:|---|
| Bronze (~1 vCPU / 2 GiB) | B1ms burstable | ~138 | M10 (0.5 vCPU) | ~174 | Mongo +26% (PG credit-throttled) |
| Silver (2 vCPU / ~8 GiB) | D2ds_v5 (2 / 8) | ~473 | M20 (1 / 4) | ~432 | **PG +9.5%** despite 2× vCPU advantage |
| Gold (4 vCPU / 16 GiB) | D4ds_v5 | ~2,616 | M40 | ~2,553 | ≈ parity (PG +2.5%) |
| Platinum (8 vCPU / 32 GiB) | D8ds_v5 | ~5,330 | M50 | ~5,147 | ≈ parity (PG +3.6%) |

## Silver-Tier Deep Dive — PG Silver vs Mongo Silver

| Metric | Postgres Silver (D2ds_v5) | Mongo Silver (M20) |
|---|---|---|
| Compute | **2 vCPU / 8 GiB** | 1 vCPU / 4 GiB |
| HA | off | off |
| Aggregate RPS | ~473 | ~432 |
| DB CPU peak | 47.79% (under band) | **78.8%** (✅ in band) |
| DB Memory peak | 33.63% (under band) | 61.1% (✅ in band) |
| Saturation verdict | DB has 50%+ headroom — **workload-limited, not tier-limited** | **DB is the cap** |

The headline: **PG Silver has 2× the vCPU and 2× the RAM yet only delivers ~10% more RPS than Mongo Silver**. PG Silver's CPU never crossed 60% — the Postgres tier is *not the bottleneck* at this offered load. The cap is upstream (app tier or load profile), so the comparison is apples-to-oranges: Mongo Silver is genuinely tier-bound at 432 RPS while PG Silver could sustain notably more if the offered load were raised. To make the Silver-vs-Silver comparison fair, PG Silver needs a re-test at a higher offered RPS until CPU lands in the 60–80% band.

## Key Takeaways

- **At ≥4 vCPU the engines converge** to ~2.5–5.3k RPS for this CRUD-on-Account shape; engine choice is a wash on throughput.
- **Mongo's per-vCPU efficiency at small tiers is striking** — 1 vCPU M20 nearly matches 2 vCPU PG Silver because PG Silver was not actually saturated.
- **Workload is uniformly CPU-bound past Bronze** on both engines; memory headroom grows at every step up.
- **The relabel exposes M40 as imperfectly Gold** — its 86.9% CPU peak overshot the band; PG Gold landed cleanly at 79.35%. Apples-to-apples at the same compute, **PG Gold is more comfortably in-band than Mongo Gold**.
- **Action item:** PG Silver re-test with a higher offered ramp would tighten the cross-engine comparison; today's PG Silver number is a lower bound on what D2ds_v5 can do.
