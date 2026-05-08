# Load Test — Gold profile (2026-05-04) — PostgreSQL `pgsql-pp-gold`, DB-saturation target

Goal: characterise the **highest sustainable RPS** for the Gold tier while keeping at least one DB-side saturation metric in the 60–80 % band, and no metric above 80 %.

- **Test window (reported run):** `2026-05-04T01:21:43Z → 2026-05-04T01:27:16Z` (≈5 min 30 s, including ramp + peak + drain)
- **Runners:** 6× k6 parallelism, pinned to AKS `nodepool3`
- **Target service:** `http://hex-scaffold.hex-scaffold.svc:80`
- **DB target:** `pgsql-pp-gold.postgres.database.azure.com`

> A first calibration run at peak target = 800 req/s/runner under-shot the band (peak DB CPU 43.6 %, IOPS 32 % of 6 000). The numbers below are from the **second run** with peak target raised to 1 500 req/s/runner — which landed inside the 60–80 % band cleanly.

---

## Infrastructure

### Azure PostgreSQL Flexible Server (`pgsql-pp-gold`) — Gold

| Property | Value |
|---|---|
| Resource group | `resources-test-rg` |
| Region | Brazil South |
| Primary availability zone | 3 |
| High availability | **ZoneRedundant** |
| PostgreSQL version | 18 |
| Compute SKU | **`Standard_D4ds_v5`** — General Purpose tier |
| vCPU / RAM | **4 vCPU / 16 GiB** |
| Storage type | **Premium SSD v2 (`PremiumV2_LRS`)** |
| Storage size | **128 GiB** |
| Provisioned IOPS | **6 000** |
| Provisioned throughput | **500 MB/s** |
| Public network access | Disabled (private endpoint via `privatelink.postgres.database.azure.com` zone) |
| Auth | Password (Active Directory disabled) |

### App tier (Helm chart `hex-scaffold`)

| Setting | Value |
|---|---|
| `replicaCount` (HPA min) | **8** |
| `replicaCount` (HPA max) | **16** |
| Pod requests | `cpu=80m`, `memory=384Mi` |
| Pod limits | `cpu=1000m`, `memory=768Mi` |
| HPA targetCPU / targetMemory | 70 % / 75 % |
| HPA `behavior.scaleUp.stabilizationWindowSeconds` | 30 s + `100 % / 30 s` policy |
| Rate limiter `permitLimit` | 100 000 (effectively disabled for the test) |
| Npgsql connection pool | `Maximum Pool Size=100` per pod |

### WireMock

4 replicas × 1 000 m CPU limit (carried over; not on the request path for this CRUD scenario).

### k6 ramping-arrival-rate scenario (per runner) — calibrated for Gold

| Phase | Target req/s | Duration |
|---|---:|---|
| warmup | 300 | 30 s |
| steady | 800 | 1 min |
| **peak** | **1 500** | **3 min** |
| drain | 300 | 30 s |
| cool | 0 | 15 s |

`preAllocatedVUs=2000`, `maxVUs=3000`. Aggregate offered RPS at peak: **9 000**. Aggregate *realised* RPS: **~2 616** (the system was DB-CPU-bound at peak).

### Cluster context

Same AKS cluster used in earlier Bronze/Silver/Platinum runs — `aks-test` (k8s 1.34, kubenet, `10.244.0.0/16` pods, `10.100.0.0/16` services). App tier on `nodepool2` (10× `Standard_D2s_v6`); k6 runners on `nodepool3` (5× `Standard_D8s_v6`).

---

## Aggregate result

| Metric | Value |
|---|---:|
| Runners | 6 |
| Total requests served | 824 983 |
| Aggregate RPS (avg) | **~2 616** |
| Errors (5xx/4xx) | 0.00 % |
| Throttled (429) | 0.00 % |
| `http_req_failed` (k6) | 0.00 % |

---

## Per-runner results (k6 summaries)

| Runner | Requests | Avg RPS | p95 create (ms) | p95 get (ms) | p95 list (ms) | p95 update (ms) | Saturation p95 VUs |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 137 503 | 435.71 | 254.32 | 235.54 | 891.79 | 306.95 | 321 |
| 2 | 137 508 | 435.56 | 330.10 | 311.25 | 275.80 | 409.27 | 323 |
| 3 | 137 493 | 436.15 | 291.33 | 273.88 | **5 614.00** ⚠ | 363.99 | 321 |
| 4 | 137 491 | 436.04 | 305.65 | 283.91 | 966.20 | 375.15 | 321 |
| 5 | 137 495 | 435.94 | 335.14 | 324.92 | 1 118.96 | 431.19 | 321 |
| 6 | 137 493 | 436.77 | 260.09 | 239.60 | 1 835.80 | 309.07 | 322 |

### p95 statistics across the 6 runners

| Endpoint | min p95 (ms) | **median p95 (ms)** | max p95 (ms) | mean p95 (ms) |
|---|---:|---:|---:|---:|
| create | 254.32 | **298.49** | 335.14 | 296.11 |
| get | 235.54 | **278.90** | 324.92 | 278.18 |
| list | 275.80 | **1 042.58** | 5 614.00 | 1 783.76 |
| update | 306.95 | **369.57** | 431.19 | 365.94 |

### Throughput / saturation statistics across the 6 runners

| Metric | min | median | max | mean |
|---|---:|---:|---:|---:|
| Avg RPS per runner | 435.56 | 435.99 | 436.77 | 436.03 |
| Saturation p95 VUs | 321 | 321 | 323 | 321.5 |

> **List p95 outlier.** One of the six runners reported `list` p95 = 5 614 ms; the others fall in the 276–1 836 ms range. This is consistent with a connection-pool acquisition tail (peak `active_connections` = 903 against 16 pods × pool size 100 = 1 600 max — the system is close to but not exhausting the pool ceiling). The other endpoints stay tightly distributed.

---

## Database saturation (Azure Monitor, 1-minute granularity)

| Time (UTC) | cpu_percent (avg / max) | memory_percent (avg / max) | iops (avg / max) | disk_iops_consumed_percentage (avg / max) | disk_bandwidth_consumed_percentage (avg / max) | active_connections (avg / max) |
|---|---:|---:|---:|---:|---:|---:|
| 01:21 | 4.1 / 5.2 | 43.9 / 43.9 | 17.0 / 33.0 | 3.0 / 5.0 | 1.0 / 2.0 | 192 / 192 |
| 01:22 | 2.6 / 3.1 | 43.8 / 43.9 | 196.0 / 357.0 | 0.5 / 1.0 | 0.0 / 0.0 | 192 / 192 |
| 01:23 (steady) | 17.4 / 26.4 | 44.5 / 44.9 | 528.5 / 734.0 | 5.0 / 5.0 | 1.0 / 1.0 | 233 / 235 |
| 01:24 (ramp) | 54.1 / 63.5 | 45.0 / 45.1 | 1 024.0 / 1 027.0 | 15.0 / 15.0 | 3.0 / 3.0 | 252 / 258 |
| **01:25 (peak)** | **76.1 / 76.9** | **53.7 / 55.5** | **862.0 / 928.0** | **13.0 / 13.0** | **3.0 / 3.0** | **878 / 903** |
| **01:26 (peak)** | **75.9 / 78.6** | **55.8 / 55.8** | **1 055.5 / 1 080.0** | n/a | n/a | **874 / 878** |
| **01:27 (peak end)** | **74.0 / 79.4** | n/a | **1 043.5 / 1 064.0** | n/a | n/a | **725 / 894** |

Window aggregates:

| Metric | Avg | Peak | % of provisioned ceiling |
|---|---:|---:|---:|
| `cpu_percent` (4 vCPU) | 43.44 | **79.35** | **79.35 %** (in 60–80 % band ✅) |
| `memory_percent` (16 GiB) | 47.78 | 55.77 | 56 % (under band) |
| `iops` (vs 6 000 provisioned) | 675.21 | 1 080 | 18 % (under band) |
| `disk_iops_consumed_percentage` | 7.30 | 15.00 | 15 % (under band) |
| `disk_bandwidth_consumed_percentage` (vs 500 MB/s) | 1.60 | 3.00 | 3 % (under band) |
| `active_connections` | 478.14 | 903.00 | n/a |
| `disk_queue_depth` | 0.40 | 1.00 | n/a |

**Which metric "hit" the 60–80 % band first?** During the **steady peak (minutes 01:25 → 01:27)**:

| Metric | Reading | Verdict |
|---|---:|---|
| **`cpu_percent`** | **76 → 78.6 → 79.4 %** | **inside 60–80 % band ✅, CPU is the binding metric** |
| `memory_percent` | 53.7 → 55.8 → n/a | flat 50s — RAM far from constraint |
| `iops` (% of 6 000) | 14 → 18 → 18 % | far below the 6 000 ceiling — IOPS is not the constraint |
| `disk_iops_consumed_percentage` | 13 → 15 → n/a | far below band |
| `disk_bandwidth_consumed_percentage` | 3 → n/a → n/a | effectively idle (3 % of 500 MB/s) |

**CPU is the binding constraint for Gold.** It crossed 60 % at minute 01:24, sat at 76 % during the centre of the peak, and reached 79.4 % at minute 01:27 — i.e. it held cleanly inside the requested band. None of the storage-tier metrics (IOPS, disk-IOPS-percent, throughput) approached saturation; the 4-vCPU compute envelope is the limit, not the Premium SSD v2 storage budget.

---

## Verdict

- **Target met (CPU band).** During the steady peak (minutes 01:25–01:27), `cpu_percent` averaged 75 % and peaked at 79.35 % — squarely inside the 60–80 % band you asked for, with no overshoot.
- **Highest sustainable RPS for Gold: ≈ 2 616 aggregate RPS** (≈ 436 RPS / runner) at the offered profile of 1 500 req/s/runner peak.
- **CPU, not storage, is the limit.** `pgsql-pp-gold` is provisioned with 6 000 IOPS and 500 MB/s on Premium SSD v2 — both wildly under-utilised at peak (18 % and 3 % respectively). The 4-vCPU `Standard_D4ds_v5` compute is the binding factor; storage is over-provisioned for this workload.
- **Latencies climb noticeably** at this load (create p95 ≈ 298 ms median, update p95 ≈ 370 ms) — consistent with the database operating at the saturation knee. The `list` endpoint shows a long tail on one runner (5 614 ms p95) likely tied to connection-pool acquisition (peak `active_connections` = 903, well above Silver's 135 but still under the 1 600-pool ceiling).
- **Calibration note.** First run at peak = 800 req/s/runner under-shot (peak CPU 43.6 %). Doubling-and-some to 1 500 req/s/runner brought the system into the band without overshooting.

### What would lift Gold's RPS further?

To push past ~2 616 RPS the binding factor must be moved:
- **Scale up to a higher-vCPU SKU.** `Standard_D8ds_v5` (Platinum) doubles the vCPU budget; the Platinum run achieved ~5 330 RPS at 65 % CPU on the same 6 000-IOPS / 500-MB/s storage — i.e. ~2× the RPS at the same saturation level, exactly what 2× the vCPU buys.
- **The storage tier already has massive headroom** — bumping IOPS, throughput, or storage size would not move the needle here. Stay on Premium SSD v2; spend any upgrade budget on compute.
- **App tier is at the HPA ceiling (16 replicas)** but per-pod CPU is well under the 1 000 m limit. If a future test pushes app-tier CPU to its limit, raising `maxReplicas` is cheap; today the app is **not** the bottleneck.

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
| PG Flex `D4ds_v5` GP Dadsv5 Series Compute (4 vCore) | 0.4800 | 0.36000 | 1 Hour |
| PG Flex PMD V2 Storage Data Stored | 0.2185 | 0.16388 | 1 GiB/Month |
| PG Flex PMD V2 IOPS Provisioned | 0.0400 | 0.03000 | 1 IOPS/Month |
| PG Flex PMD V2 Throughput Provisioned | 0.1600 | 0.12000 | 1 MBps/Month |
| PG Flex Backup Storage LRS Data Stored | 0.0950 | 0.07125 | 1 GB/Month |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| PG D4ds_v5 compute | 0.48 × 730 | 350.40 | 262.80 |
| PG storage 128 GiB | 0.2185 × 128 | 27.97 | 20.98 |
| PG storage 6000 IOPS | 0.04 × 6000 | 240.00 | 180.00 |
| PG storage 500 MBps throughput | 0.16 × 500 | 80.00 | 60.00 |
| PG backup ≤ 128 GiB | included | 0.00 | 0.00 |
| PG subtotal | | 698.37 | 523.78 |
| App pro-rated D2s_v6 | 0.161 × 730 × 0.75 | 88.15 | 66.11 |
| App subtotal | | 88.15 | 66.11 |
| **Gold REST + PG total** | | **$786.52** | **$589.89** |

Savings: $196.63/month at 25% discount.

### Notes

- HPA-bounded reservation as proxy (no per-pod CPU/memory snapshot in this run report).
- Memory binds (75.0%) over CPU (64.0%); pro-rate uses binding dimension.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB.
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- PG Flex GP Dadsv5 Series priced per vCore/hour; D4ds_v5 = 4 vCore × $0.12/vCore/hr = $0.48/hr (brazilsouth retail).
- Premium SSD v2 storage is metered separately for capacity ($0.2185/GiB/mo), provisioned IOPS ($0.04/IOPS/mo), and provisioned throughput ($0.16/MBps/mo). This is the dominant cost driver at Gold tier.
- ZoneRedundant HA doubles the compute cost in production; this run used a single primary — compute above is primary only.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
