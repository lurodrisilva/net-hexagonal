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

- [ ] To push PG Platinum+ into the saturation band, raise HPA `maxReplicas` above 64 OR add a per-pod CPU limit bump (current 1500m may be hit). Re-test with HPA max 96 or 128.
- [ ] Profile EF Core / Npgsql path on the app side under load — the CPU-per-request gap to the Mongo driver is the dominant cost at this scale.
- [ ] PG Gold (D4ds_v5) is likely the cost-optimal tier for this CRUD shape if the app tier scales correspondingly. Worth a re-test of PG Gold with the Platinum+ app footprint to validate.
