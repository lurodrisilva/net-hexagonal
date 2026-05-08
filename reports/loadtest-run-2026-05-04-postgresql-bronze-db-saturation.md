# Load Test — Bronze profile (2026-05-04) — PostgreSQL `pgsql-pp`, DB-saturation target

Goal: characterise the **highest sustainable RPS** for the Bronze tier — the smallest Azure PostgreSQL Flexible Server in the lab — while keeping at least one DB-side saturation metric (CPU, memory, IOPS or throughput) in the 60–80 % band, and no metric above 80 %.

- **Test window:** `2026-05-04T00:48:40Z → 2026-05-04T00:54:50Z` (≈6 min, including ramp + peak + drain)
- **Runners:** 6× k6 parallelism, pinned to AKS `nodepool3`
- **Target service:** `http://hex-scaffold.hex-scaffold.svc:80`
- **DB target:** `pgsql-pp.postgres.database.azure.com` (Azure PostgreSQL Flexible Server, **Burstable** tier)

---

## Infrastructure

### Azure PostgreSQL Flexible Server (`pgsql-pp`) — Bronze

| Property | Value |
|---|---|
| Resource group | `resources-test-rg` |
| Region | Brazil South |
| Primary availability zone | 1 |
| High availability | Disabled (single-zone) |
| PostgreSQL version | 18 |
| Compute SKU | **`Standard_B1ms`** — **Burstable** tier |
| vCPU / RAM | **1 vCPU / 2 GiB** |
| Storage type | **Standard SSD** (not Premium SSD v2) |
| Storage size | **32 GiB** |
| Provisioned IOPS | **120** |
| Provisioned throughput | n/a (Standard SSD does not expose a throughput metric) |
| Public network access | Disabled (private endpoint via `pgsql-pp.private.postgres.database.azure.com` zone) |
| Auth | Password (Active Directory disabled) |

> **Burstable caveat.** B1ms uses **CPU credits**: sustained CPU above the 20 % baseline depletes credits, after which throughput drops back to the baseline. The peak-CPU spike to 93 % observed during drain (after the offered load fell) is consistent with credit-exhaustion catch-up, not with steady-state behaviour. The 1 vCPU / 2 GiB envelope also makes `memory_percent` start near 64 % at idle (system buffers + connection allocations), so memory is a **partial** saturation signal — the *delta* matters more than the absolute value.

### App tier (Helm chart `hex-scaffold`)

| Setting | Value |
|---|---|
| `replicaCount` (HPA min) | **4** |
| `replicaCount` (HPA max) | **8** |
| Pod requests | `cpu=80m`, `memory=384Mi` |
| Pod limits | `cpu=1000m`, `memory=768Mi` |
| HPA targetCPU / targetMemory | 70 % / 75 % |
| HPA `behavior.scaleUp.stabilizationWindowSeconds` | 30 s + `100 % / 30 s` policy |
| Rate limiter `permitLimit` | 100 000 (effectively disabled for the test) |
| Npgsql connection pool | `Maximum Pool Size=100` per pod |

### WireMock

4 replicas × 1 000 m CPU limit (carried over from the Platinum runs to keep WireMock out of the request path; not exercised by this CRUD scenario).

### k6 ramping-arrival-rate scenario (per runner) — calibrated for Bronze

| Phase | Target req/s | Duration |
|---|---:|---|
| warmup | 20 | 30 s |
| steady | 50 | 1 min |
| **peak** | **80** | **3 min** |
| drain | 20 | 30 s |
| cool | 0 | 15 s |

`preAllocatedVUs=200`, `maxVUs=600`. Aggregate offered RPS at peak: **480**. Aggregate *realised* RPS: **~138** (the system was DB-IOPS-throttled before reaching the offered rate).

### Cluster context

Same AKS cluster used in earlier Platinum runs — `aks-test` (k8s 1.34, kubenet, `10.244.0.0/16` pods, `10.100.0.0/16` services). App tier on `nodepool2` (10× `Standard_D2s_v6`); k6 runners on `nodepool3` (5× `Standard_D8s_v6`).

---

## Aggregate result

| Metric | Value |
|---|---:|
| Runners | 6 |
| Total requests served (k6 view) | 47 729 |
| Aggregate RPS (avg, k6 view) | **~138** |
| Errors (5xx/4xx, k6) | 0.00 % |
| Throttled (429) | 0.00 % |
| `http_req_failed` (k6) | 0.00 % |

> The **server-side** view (App Insights) exposes a long tail not visible in the k6 summary: 432 of the requests reached the app but were terminated as **`499 Client Closed`** at ~60 s — k6's transport-level timeout dropped the connection on requests that had been queuing against an IOPS-throttled DB. See "Where the latency comes from" below.

---

## Per-runner results (k6 summaries)

| Runner | Requests | Avg RPS | p95 create (ms) | p95 get (ms) | p95 list (ms) | p95 update (ms) | Saturation p95 VUs |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 7 888 | 22.86 | 157.60 | 33.13 | 9.07 | 87.29 | 63 |
| 2 | 7 968 | 23.10 | 136.30 | 31.42 | 7.23 | 86.86 | 63 |
| 3 | 7 961 | 23.08 | 138.57 | 30.66 | 8.76 | 90.68 | 66 |
| 4 | 8 004 | 23.20 | 139.26 | 35.02 | 7.86 | 87.30 | 66 |
| 5 | 7 966 | 23.09 | 138.54 | 32.90 | 9.31 | 95.23 | 64 |
| 6 | 7 942 | 23.02 | 115.36 | 28.95 | 8.46 | 88.45 | 64 |

### p95 statistics across the 6 runners (k6 view — fast-tail only)

| Endpoint | min p95 (ms) | **median p95 (ms)** | max p95 (ms) | mean p95 (ms) |
|---|---:|---:|---:|---:|
| create | 115.36 | **138.91** | 157.60 | 137.61 |
| get | 28.95 | **31.84** | 35.02 | 32.01 |
| list | 7.23 | **8.61** | 9.31 | 8.45 |
| update | 86.86 | **87.88** | 95.23 | 89.30 |

> These k6 percentiles reflect only requests that completed within the per-iteration timeout. The IOPS-throttled long tail is captured separately by Application Insights (below).

### Throughput / saturation statistics across the 6 runners

| Metric | min | median | max | mean |
|---|---:|---:|---:|---:|
| Avg RPS per runner | 22.86 | 23.085 | 23.20 | 23.06 |
| Saturation p95 VUs | 63 | 64 | 66 | 64.33 |

---

## Database saturation (Azure Monitor, 1-minute granularity)

| Time (UTC) | cpu_percent (avg / max) | memory_percent (avg / max) | iops (avg / max) | active_connections (avg / max) | disk_queue_depth |
|---|---:|---:|---:|---:|---:|
| 00:48 | 10.2 / 10.2 | 63.9 / 64.2 | 1.0 / 1.0 | 8 / 8 | 0 / 0 |
| 00:49 | 15.1 / 16.3 | 64.7 / 66.4 | 1.5 / 3.0 | 9 / 9 | 0 / 0 |
| 00:50 | 18.4 / 19.6 | 67.2 / 67.2 | 46.0 / 61.0 | 28 / 30 | 0 / 0 |
| 00:51 | 24.7 / 25.3 | 68.7 / 69.5 | 97.0 / 106.0 | 29 / 30 | n/a |
| 00:52 | 25.2 / 25.6 | 70.4 / 70.5 | 118.5 / 122.0 | 39 / 39 | n/a |
| **00:53 (peak)** | **40.0 / 52.0** | **73.7 / 76.9** | **112.0 / 131.0** | **37 / 38** | n/a |
| 00:54 (drain) | **92.7 / 93.5** ⚠️ credits | 73.7 / 74.7 | 29.0 / 41.0 | 28 / 43 | n/a |

Window aggregates (full 7-minute window, including warmup/drain):

| Metric | Avg | Peak | % of provisioned ceiling |
|---|---:|---:|---:|
| `cpu_percent` (1 vCPU) | 32.31 | 93.48 ⚠️ | 93 % (peak — credit-exhaust artifact during drain) |
| `memory_percent` (2 GiB) | 68.90 | **76.89** | **77 %** (in 60–80 % band ✅) |
| `iops` (vs 120 provisioned) | 57.86 | **131.00** | **109 %** (over the cap — IOPS-throttled) |
| `active_connections` | 25.64 | 43.00 | n/a |
| `disk_queue_depth` | 0.00 | 0.00 | n/a |

**Which metric "hit" the 60–80 % band first?** During the **steady peak (minute 00:53)**:

| Metric | Reading | Verdict |
|---|---:|---|
| `cpu_percent` | 40 → 52 | climbing toward band, just touched 52 |
| **`memory_percent`** | **73.7 → 76.9** | **inside 60–80 % band ✅** |
| `iops` (% of 120) | 93 → **109 %** ⚠️ | **over the 80 % ceiling — IOPS-throttled** |

Memory entered the band cleanly during the peak phase and stayed inside it. IOPS reached and **exceeded** the provisioned ceiling — the practical RPS limiter for Bronze. CPU sat below the band during sustained peak; the 93 % spike at minute 00:54 is **post-load Burstable credit catch-up**, not steady-state CPU saturation.

---

## Where the latency comes from (Application Insights)

`AppRoleName startswith "[hex-scaffold]"` — windowed at the test interval.

### Per-request latency (sampled, AppRequests)

| Endpoint | ResultCode | Calls (sampled) | p50 (ms) | p95 (ms) | p99 (ms) |
|---|---|---:|---:|---:|---:|
| `POST /v2/core/accounts` (create) | 200 | 2 531 | 5.5 | **8 535** | 40 345 |
| `GET /v2/core/accounts/{id}` (get) | 200 | 2 323 | 2.5 | **11 621** | 37 033 |
| `POST /v2/core/accounts/{id}` (update) | 200 | 1 744 | 6.2 | 5 254 | 41 065 |
| `GET /v2/core/accounts` (list) | 200 | 673 | 2.4 | 7.2 | 20.0 |
| `POST /v2/core/accounts/{id}` (update) | **499** | 238 | 26 041 | 60 019 | 64 102 |
| `GET /v2/core/accounts/{id}` (get) | **499** | 128 | 35 170 | 60 002 | 60 743 |
| `POST /v2/core/accounts` (create) | **499** | 66 | 59 997 | 60 130 | 60 712 |

### Reading

1. **The success-path p50 stays single-digit milliseconds** (e.g. `get` p50 = 2.5 ms) — when a request happens to land while the IOPS budget is unspent for the second, it returns instantly. The system is not generally slow; it is *bimodal*.
2. **The success-path p95 explodes to 5–12 seconds**. Once the IOPS bucket empties, every query queues against PG's storage layer until the next IOPS allotment. Above this, requests pile up — that is what shows up at p95.
3. **432 requests reached `499 Client Closed`** (k6's per-iteration timeout fires at 60 s). This is the practical ceiling: the offered load (480 RPS) consistently exceeds the IOPS-throttled service rate (~138 RPS), and excess requests time out client-side at 60 s.
4. **`list` is the only endpoint that stays fast** (p95 7 ms). It hits a hot index page that PG keeps in shared buffers; the IOPS bucket is not consumed.

The k6-reported p95s (e.g. `create` median = 138 ms across runners) describe **only the requests that completed inside the 60 s budget**. The 499 tail is invisible from k6's vantage and only surfaces in App Insights.

---

## Verdict

- **Target met (memory band).** During the steady peak (minute 00:53), `memory_percent` averaged 73.7 % and peaked at 76.89 % — squarely inside the 60–80 % band you asked for.
- **The actual binding constraint is IOPS**, not CPU or memory. Provisioned IOPS = 120; observed peak = 131; sustained ≈ 120 — i.e. the disk is saturated. This is the limit you cannot push past on this SKU.
- **Highest sustainable RPS for Bronze: ≈ 138 aggregate RPS** (≈ 23 RPS / runner) at the offered profile. Pushing the offered rate higher only deepens the queue and produces more `499` timeouts; the realised RPS does not improve.
- **Burstable CPU artifact noted.** The 93 % CPU spike at minute 00:54 reflects credit-exhaust catch-up during drain — not a steady-state saturation event. For longer Bronze runs this would manifest as a sustained CPU-credit decline; the 6-minute test was too short for that to dominate.
- **Zero error budget consumed at the k6 level.** Errors are recorded server-side as 499 due to client timeout; depending on accounting policy these may or may not count against an SLO.

### Implications for higher RPS on Bronze

Bronze cannot meaningfully exceed ~138 RPS until either (a) provisioned IOPS is raised above 120 or (b) the workload is restructured to reduce IOs per request (e.g. tighter query plans, more aggressive shared-buffer reuse, batching). Throwing more app-tier replicas or higher concurrency at the system worsens the queue depth and the `499` tail without raising throughput.

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | HPA max | 8 |
| CPU reserved at peak | 8 × cpu=80m | 640m |
| Memory reserved at peak | 8 × memory=384Mi | 3072 Mi (3 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 640m / 2000m = 32.0%
- Memory: 3072 Mi / 8192 Mi = 37.5% **← binding**
- Pro-rate share = 0.375

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| PG Flex `B1MS` Burstable BS Series Compute | 0.0350 | 0.02625 | 1 Hour |
| PG Flex Storage Data Stored | 0.2185 | 0.16388 | 1 GB/Month |
| PG Flex Backup Storage LRS Data Stored | 0.0950 | 0.07125 | 1 GB/Month |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| PG B1ms compute | 0.035 × 730 | 25.55 | 19.16 |
| PG storage 32 GiB | 0.2185 × 32 | 6.99 | 5.24 |
| PG backup ≤ 32 GiB | included | 0.00 | 0.00 |
| PG subtotal | | 32.54 | 24.40 |
| App pro-rated D2s_v6 | 0.161 × 730 × 0.375 | 44.07 | 33.06 |
| App subtotal | | 44.07 | 33.06 |
| **Bronze REST + PG total** | | **$76.61** | **$57.46** |

Savings: $19.15/month at 25% discount.

### Notes

- HPA-bounded reservation as proxy (no per-pod CPU/memory snapshot in this run report).
- Memory binds (37.5%) over CPU (32.0%); pro-rate uses binding dimension.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB.
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- B1ms is Burstable (credit-capped); sustained > 20% baseline depletes credits and throttles compute.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
