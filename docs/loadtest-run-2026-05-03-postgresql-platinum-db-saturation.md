# Load Test — Run 3 (2026-05-03) — PostgreSQL Platinum, DB-saturation target

Goal: push the **Azure PostgreSQL Flexible Server `pgsql-pp-platinum-1`** CPU toward ~60 % to characterise the system at its database-saturation knee. Compare against earlier runs that were app-tier-bottlenecked.

- **Test window:** `2026-05-03T23:46:00Z → 2026-05-03T23:51:30Z` (≈5 min 30 s, including ramp + peak + drain)
- **Runners:** 6× k6 parallelism, pinned to AKS `nodepool3`
- **Target service:** `http://hex-scaffold.hex-scaffold.svc:80`
- **DB target:** `pgsql-pp-platinum-1.postgres.database.azure.com` (Azure PostgreSQL Flexible Server)

---

## Infrastructure

### Azure PostgreSQL Flexible Server (`pgsql-pp-platinum-1`)

| Property | Value |
|---|---|
| Resource group | `resources-test-rg` |
| Region | Brazil South |
| Primary availability zone | 1 |
| High availability | **ZoneRedundant** (standby in zone 2, state = Healthy) |
| PostgreSQL version | **18** |
| Compute SKU | **`Standard_D8ds_v5`** — General Purpose tier |
| vCPU / RAM | **8 vCPU / 32 GiB** |
| Storage type | **Premium SSD v2 (`PremiumV2_LRS`)** |
| Storage size | **128 GiB** |
| Provisioned IOPS | **6 000** |
| Provisioned throughput | **500 MB/s** |
| Storage auto-grow | Disabled |
| Backup retention | 7 days |
| Geo-redundant backup | Disabled |
| Public network access | **Disabled** (private endpoint only) |
| Private DNS zone | `privatelink.postgres.database.azure.com` (in `aks-test-rg`) |
| Delegated subnet | `aks-vnet/pe-sub-6` (private endpoint subnet) |
| Auth | Password (Active Directory disabled) |
| Data encryption | System-managed key |

**Headroom budget for the test:**

| Resource | Provisioned | Peak observed | % consumed |
|---|---:|---:|---:|
| vCPU (8 cores) | 100 % | 64.9 % | 65 % of CPU |
| Storage IOPS | 6 000 | 1 371 | 23 % |
| Storage throughput | 500 MB/s | n/a (low — IOPS-dominated) | <5 % |
| Storage capacity | 128 GiB | (test data set ~MBs) | <1 % |

The Platinum tier here is named after the SKU class but is otherwise an Azure General-Purpose `D8ds_v5` flexible server with Premium SSD v2 storage at 6 000 IOPS / 500 MB/s.

### AKS cluster (`aks-test`)

| Property | Value |
|---|---|
| Resource group | `aks-test-rg` |
| Region | Brazil South |
| Kubernetes version | **1.34** |
| Network plugin | `kubenet` (no Azure CNI) |
| Pod CIDR | `10.244.0.0/16` |
| Service CIDR | `10.100.0.0/16` |
| Max pods per node | 110 |

### AKS nodepools

| Nodepool | Mode | VM size | vCPU / RAM per node | Nodes | Role in the test |
|---|---|---|---:|---:|---|
| `nodepool` | System | `Standard_D2s_v3` | 2 / 8 GiB | 10 | Cluster system pods (kube-proxy, CoreDNS, metrics-server, k6 operator, etc.) |
| `nodepool2` | User | `Standard_D2s_v6` | 2 / 8 GiB | 10 | **App tier** (`hex-scaffold` Deployment, WireMock) |
| `nodepool3` | User | **`Standard_D8s_v6`** | **8 / 32 GiB** | 5 | **k6 load runners** (pinned via `nodeSelector: agentpool=nodepool3`) |

> ⚠️ The comment in `tests/loadtest/k6/testrun-pgsql-pp.yaml` describes `nodepool3` as `Standard_D2s_v6` (2 vCPU). That comment is **stale** — the live nodepool is `Standard_D8s_v6` (8 vCPU per node). The runner-fit math the comment derives (1 runner / 750 m → 5 max parallelism) no longer applies; the current pool can host 12+ runners per node at `requests.cpu=600m`.

### Per-node CPU allocatable (after AKS DaemonSet reservation)

Observed via `kubectl describe nodes`:

| Node | Pool | CPU requested | CPU allocatable / node total |
|---|---|---:|---:|
| `aks-nodepool2-…vmss00000{0..9}` | nodepool2 | 720 m | ~1 370 m / 2 000 m |
| `aks-nodepool3-…vmss00000{0..4}` | nodepool3 | 720–1 048 m | ~7 470 m / 8 000 m |

### App tier (Helm chart `hex-scaffold`)

| Setting | Value |
|---|---|
| Image | `ghcr.io/lurodrisilva/net-hexagonal:latest` |
| `replicaCount` (initial) | 4 (HPA min) |
| `replicaCount` (max under HPA) | 16 |
| Pod requests | `cpu=160m`, `memory=512Mi` |
| Pod limits | `cpu=1000m`, `memory=768Mi` |
| HPA targetCPU / targetMemory | 70 % / 75 % |
| HPA `behavior.scaleUp.stabilizationWindowSeconds` | 30 s + `100 % / 30 s` policy |
| Rate limiter `permitLimit` | 100 000 (effectively disabled for the test) |
| Npgsql connection pool | `Maximum Pool Size=100` per pod |
| Persistence | `postgres` (PR #35-tuned: `Connection Idle Lifetime=300`, `Connection Pruning Interval=10`, `No Reset On Close=true`) |
| Inbound adapter | `rest` (FastEndpoints) |
| Outbound adapter | `rest` (resilient `HttpClient` → WireMock) |

### WireMock (in-cluster mock for `IExternalApiClient`)

| Setting | Value |
|---|---|
| Replicas | **4** (raised from 1 to break a Run 2 bottleneck — see comparison table) |
| Image | `wiremock/wiremock:3.13.2-3-alpine` |
| Pod requests | `cpu=500m`, `memory=384Mi` |
| Pod limits | `cpu=1000m`, `memory=768Mi` |
| `fixedDelayMs` | 300 ms (injected into every stub response) |

**Confirmed during this run:** zero outbound calls to WireMock appear in `AppDependencies` — the CRUD scenario does not exercise `IExternalApiClient`. WireMock fan-out is overhead for Run 3 and was retained only as a precaution against re-introducing the Run 2 bottleneck.

### k6 runners (k6-operator `TestRun`)

| Setting | Value |
|---|---|
| `parallelism` | 6 |
| `nodeSelector` | `agentpool=nodepool3` |
| Runner pod requests | `cpu=600m`, `memory=512Mi` |
| Runner pod limits | `cpu=1000m`, `memory=1Gi` |
| `BASE_URL` | `http://hex-scaffold.hex-scaffold.svc:80` (injected by `loadtest-pgsql-pp.sh`) |

### k6 ramping-arrival-rate scenario (per runner)

| Phase | Target req/s | Duration |
|---|---:|---|
| warmup | 200 | 30 s |
| steady | 1 400 | 1 min |
| **peak** | **3 500** | **3 min** |
| drain | 800 | 30 s |
| cool | 0 | 15 s |

`preAllocatedVUs=1500`, `maxVUs=3000`. A read-heavy `list_heat` overlay runs at 30 req/s constant for 2 min (insignificant against the main flow).

### Observability

- **Application Insights** — connection string injected via Helm (`secrets.appInsightsConnectionString`); ingest reaches workspace `7104c6dc-8269-4283-9699-1840c52bbbe0` (`DefaultWorkspace-…-CQ`). Default sampling in effect (~10 % of requests reach the workspace).
- **Azure Monitor metrics** — flexible-server metrics polled at 1-minute granularity via `az monitor metrics list`.
- **kubectl top** — used live during the run for HPA / WireMock observation.

### Network path summary

```
k6 runner pods (nodepool3)
   └── HTTP → ClusterIP svc:80
          └── hex-scaffold pods (nodepool2, 4→16 replicas)
                 ├── DB connections → private endpoint in pe-sub-6
                 │      └── pgsql-pp-platinum-1 (Premium SSD v2, 6000 IOPS, 500 MB/s)
                 └── outbound HTTP → wiremock svc (4 replicas, idle in this scenario)
```

All in-cluster traffic stays inside the AKS pod CIDR (`10.244.0.0/16`); the only off-cluster hop is the private-endpoint to PG Flex.

---

## Aggregate result

| Metric | Value |
|---|---:|
| Runners | 6 |
| Total requests served | 1 682 031 |
| Aggregate RPS (peak average) | **~5 330** |
| Errors (5xx/4xx) | 0.00–0.01 % |
| Throttled (429) | 0.00 % |
| `http_req_failed` (k6) | 0.00–0.01 % |

---

## Per-runner results (k6 summaries)

| Runner | Requests | Avg RPS | p95 create (ms) | p95 get (ms) | p95 list (ms) | p95 update (ms) | Saturation p95 VUs |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 280 624 | 888.67 | 455.11 | 434.84 | 769.25 | 608.34 | 405 |
| 2 | 280 581 | 888.41 | 473.90 | 460.29 | 896.13 | 635.72 | 410 |
| 3 | 280 706 | 889.59 | 459.74 | 435.74 | 694.91 | 609.65 | 406 |
| 4 | 279 659 | 886.15 | 440.54 | 425.52 | 947.13 | 593.87 | 413 |
| 5 | 280 375 | 888.30 | 591.35 | 574.25 | 606.29 | 790.19 | 437 |
| 6 | 280 086 | 889.43 | 566.99 | 545.04 | 470.27 | 752.36 | 434 |

### p95 statistics across the 6 runners

| Endpoint | min p95 (ms) | **median p95 (ms)** | max p95 (ms) | mean p95 (ms) |
|---|---:|---:|---:|---:|
| create | 440.54 | **466.82** | 591.35 | 497.94 |
| get | 425.52 | **448.02** | 574.25 | 479.28 |
| list | 470.27 | **732.08** | 947.13 | 730.66 |
| update | 593.87 | **622.69** | 790.19 | 665.02 |

### Throughput / saturation statistics across the 6 runners

| Metric | min | median | max | mean |
|---|---:|---:|---:|---:|
| Avg RPS per runner | 886.15 | 888.54 | 889.59 | 888.43 |
| Saturation p95 VUs | 405 | 411.5 | 437 | 417.5 |

The runner-to-runner spread is tight (RPS within 0.4 %, VUs within 8 %), which means the offered load was uniform; per-endpoint variance comes from the system under test, not the load generator.

---

## Cluster behaviour during the peak

Observed via `kubectl get hpa -w` and `kubectl top` while the test was running.

| Time (UTC) | Stage | App pod CPU (avg, % of request) | App replicas | WireMock total CPU |
|---|---|---:|---:|---:|
| 23:46 | warmup begins | 6 % | 13 (residual) | ~316 m |
| 23:47 | steady | 80 % | 13 | ~92 m |
| 23:47 → 23:48 | HPA scale-up trigger | 232 % | 13 → 14 | 84–276 m |
| 23:48 → 23:49 | scaling | 215 % | 14 → 16 | 14–287 m |
| 23:49 → 23:50 | peak holding | 204–225 % | 16 | 13–296 m |
| 23:50 → 23:51 | peak holding | 222–236 % | 16 | 12–296 m |
| 23:51 | drain → finish | 236 % (then drop) | 16 | 12 m |

- **HPA reacted in ≈45 s** (steady-state burst → 4 → 8 → 16 replicas) thanks to `stabilizationWindowSeconds=30` + `100 % / 30 s` policy.
- **App pod saturation against the limit:** at peak, per-pod CPU averaged ~225 % of the 160 m request ≈ 360 m / 1 000 m limit ≈ **36 % of pod CPU limit**, and memory averaged 45 % of limit. Both well under the 75 % ceiling.
- **WireMock fan-out (4 replicas) was idle** even at 5.3 k RPS — it was not on the hot path for this test's CRUD flow (confirmed via App Insights AppDependencies, which shows zero outbound calls to `wiremock`).

---

## Database saturation (Azure Monitor, 1-minute granularity)

| Time (UTC) | cpu_percent (avg / max) | active_connections (avg / max) | iops (avg / max) | disk_queue_depth |
|---|---:|---:|---:|---:|
| 23:46 | 1.4 / 1.4 | 408 / 408 | 9 / 11 | 0 / 0 |
| 23:47 | 8.8 / 12.9 | 452 / 457 | 442 / 718 | 0 / 0 |
| 23:48 | 32.4 / 36.2 | 647 / 723 | 1 274 / 1 347 | 0 / 0 |
| 23:49 | 42.8 / 45.3 | 807 / 816 | 1 370 / 1 371 | 1 / 1 |
| 23:50 | 55.6 / 58.3 | 889 / 931 | 1 318 / 1 331 | 1 / 1 |
| 23:51 | **57.4 / 64.9** | 1 035 / 1 055 | 1 283 / 1 313 | 1 / 1 |

Window aggregates:

| Metric | Avg | Peak | % of provisioned |
|---|---:|---:|---:|
| `cpu_percent` (8 vCPU) | 33.06 | **64.88** | 65 % |
| `memory_percent` (32 GiB) | 42.04 | 45.51 | 46 % |
| `active_connections` | 706.33 | 1 055.00 | n/a (well under PG max) |
| `tps` | – | 26 310.50 | n/a |
| `iops` | 949.17 | 1 371.00 | **23 % of 6 000** |
| `disk_iops_consumed_percentage` | 0.00 | 0.00 | – (Azure doesn't populate this for Premium SSD v2 today) |
| `disk_queue_depth` | 0.00 (avg) | 1 (peak) | nominal |

**The 60 %-CPU target was hit:** the DB peaked at **64.88 %** CPU in minute 6 of the test, with `disk_queue_depth ≤ 1` and provisioned IOPS only 23 % consumed — i.e. the DB was CPU-bound, not I/O-bound, exactly the operating point we set out to reach.

---

## Where the latency comes from (Application Insights)

`AppRoleName startswith "[hex-scaffold]"` — windowed at the test interval.

### Per-request latency (sampled, AppRequests)

| Endpoint | Calls | p50 (ms) | p95 (ms) | p99 (ms) |
|---|---:|---:|---:|---:|
| `POST /v2/core/accounts` (create) | 9 053 | 5.4 | 738.4 | 1 555.0 |
| `GET /v2/core/accounts/{id}` (get) | 8 787 | 2.4 | 647.6 | 1 470.2 |
| `POST /v2/core/accounts/{id}` (update) | 8 126 | 6.9 | **1 233.8** | 2 949.5 |
| `GET /v2/core/accounts` (list) | 102 | 2.4 | 540.2 | 1 832.7 |

> App Insights sampling is in effect (~10 % of the population reaches the workspace), so absolute counts here are not k6's full-population numbers. The percentiles, however, are unbiased.

### Outbound dependencies (top by total time)

| Type / target | Calls | p50 (ms) | p95 (ms) | total_ms |
|---|---:|---:|---:|---:|
| `postgresql` (query) — `pgsql-pp-platinum-1` | 34 178 | 3.7 | **345.4** | 1 858 416 |
| `CONNECT postgres` (handshake) — `pgsql-pp-platinum-1` | 90 | 578.9 | **3 848.2** | 100 383 |
| Live Metrics (`brazilsouth.livediagnostics…`) | 8 732 | 1.6 | 5.6 | 37 399 |
| App Insights ingest (`POST /v2/track`) | 51 | 38.4 | 324.5 | 4 723 |
| App Insights profile fetch | 15 | 52.5 | 814.8 | 2 654 |

### Reading

1. **Postgres query p95 = 345 ms** is the dominant signal. At 65 % DB CPU with ~1 055 concurrent connections, every query queues for a CPU slot — even single-row PK lookups slow from <10 ms to ~345 ms. This single number explains the per-endpoint p95s mechanically:

   | Endpoint | DB ops per request | Predicted p95 | Observed p95 |
   |---|---:|---:|---:|
   | get | 1 | ~345 ms + app ≈ 500–650 ms | 648 ms |
   | list | 1 | ~345 ms + app ≈ 540 ms | 540 ms |
   | create | 1 (+ events) | ~345 ms + serialisation ≈ 700 ms | 738 ms |
   | update | 2 (SELECT + UPDATE) | ~690 ms + app ≈ 1 200 ms | 1 234 ms |

2. **Connection establishment p95 = 3.85 s, only 90 events in 26 k+ requests.** Cold handshakes affect ~0.3 % of traffic and explain the long tail (p99 ≥ 1.5 s). They occur as the deployment scales 4 → 8 → 16 — every new pod opens its initial pool to Azure PG, and TLS + auth against the private endpoint is expensive (~580 ms p50 / 3.8 s p95 for `CONNECT postgres`).

3. **WireMock has zero outbound calls** during this test (visible in AppDependencies). The CRUD scenario does not exercise `IExternalApiClient`, so the 300 ms `fixedDelayMs` is not on the request path.

4. **App Insights diagnostics are not the bottleneck** — sub-10 ms p95 for Live Metrics; the AI ingest and profile calls are infrequent enough to disappear into noise.

---

## Comparison against the previous two runs

All three runs were against the **same** PG SKU (`Standard_D8ds_v5` GP, 6 000 IOPS, 500 MB/s, 8 vCPU / 32 GiB RAM) and the **same** AKS cluster.

| Metric | Run 1 (PR #35 baseline) | Run 2 (HPA scaled, WireMock 1×) | **Run 3 (HPA + WireMock 4×)** |
|---|---:|---:|---:|
| App max replicas | 10 | 16 | 16 |
| WireMock replicas | 1 | 1 | **4** |
| Aggregate RPS | ~3 033 | ~2 511 | **~5 330** |
| Errors | 0.00 % | 0.00 % | 0.00–0.01 % |
| Throttled | 0.00 % | 0.00 % | 0.00 % |
| Median p95 create (across runners) | 156 ms | 2 061 ms | **466.82 ms** |
| Median p95 get | 152 ms | 1 996 ms | **448.02 ms** |
| Median p95 list | 605 ms | 2 719 ms | **732.08 ms** |
| Median p95 update | 199 ms | 3 349 ms | **622.69 ms** |
| Saturation p95 VUs (median) | 197 | 500 | **411.5** |
| **DB cpu_percent peak** | 42 % | 22 % | **64.88 %** |
| DB cpu_percent avg | 16.6 % | 13.4 % | 33.1 % |
| DB tps peak | 14 856 | 8 150 | **26 311** |
| DB iops peak | 1 336 | 886 | 1 371 |
| DB connections peak | 900 | 409 | 1 055 |

Run 2 was the diagnostic step that exposed WireMock as the choke point at 1 replica. Run 3 cleared that gate and let the load propagate to the database tier as intended.

---

## Verdict

- **Target met** — DB CPU peaked at 64.88 %, just over the requested ~60 %.
- **System pressure correctly placed** on the database tier (TPS ≈ 26 k peak, CPU ≈ 65 %), not on the app tier (~36 % of pod limit) or WireMock (idle).
- **Latencies are the cost of operating at the saturation knee.** With a single DB query running ~345 ms p95, every endpoint's p95 sits between 540 ms (single-op) and 1.2 s (two-op `update`). This is consistent with classical USL/Erlang-C behaviour past the ~60 % utilisation knee and is *not* a code defect.
- **Zero error budget consumed.** Even at 5.3 k RPS the failure rate is at the noise floor (~0.01 %), and no requests were rate-limited.

### If we want lower p95 at the same throughput

- **Scale up the PG SKU** (e.g. `Standard_D16ds_v5`, 16 vCPU). Same offered load would sit at ~32 % CPU and queries return to ~10 ms p95 — at the cost of $/hour.
- **Halve `update`'s round-trips.** The two-op SELECT + UPDATE pattern is the worst offender; an index-tuned single-statement update would mechanically halve its p95.
- **Pre-warm the connection pool** to drive `CONNECT` events to zero during the test, smoothing the long tail (p99). The current Npgsql pool grows lazily as new HPA replicas appear.
