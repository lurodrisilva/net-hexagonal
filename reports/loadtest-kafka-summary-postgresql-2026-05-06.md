# Kafka Inbound Load Test — Consolidated Summary (PostgreSQL only)

Phase 5 redo, executed 2026-05-06 against the `aks-test` cluster after physically isolating workload, broker, and load-gen lanes across separate AKS nodepools.

> **Scope note:** Only the 3 PostgreSQL tiers ran in this batch. The 3 DocumentDB tiers (silver-mongo, gold-mongo, platinum-mongo) are deferred until the `dbuser` SCRAM-SHA-256 authentication failure on `documentdb-*.mongocluster.cosmos.azure.com` is resolved (the script-default password is rejected by the live cluster as of 2026-05-06).

## Lane isolation (Phase 5 redo)

| Lane | Pool | Components |
|---|---|---|
| Workload | `nodepool2` (preferred), `nodepool` / `nodepool3` (overflow) | hex-scaffold app pods, wiremock |
| Broker + infra | `nodepool4` only | Strimzi Kafka brokers ×2, cruise-control, entity-operator, kafka-exporter |
| Load gen | `nodepool3` only | k6 TestRun runner pods |

All node taints empty across the 4 nodepools, so no tolerations required. PV zone-mismatch (`nodepool3` AZ-aware → `nodepool4` non-AZ) was resolved by deleting the broker NodePool (`deleteClaim: true` cascaded PVCs), then re-provisioning on `nodepool4` from fresh Strimzi reconcile.

## Per-tier comparison

| Tier | App pods | CPU req | CPU limit | Mem req | Mem limit | k6 offered | k6 produced | Producer p95 | Consumer rate | End-of-run lag | DB CPU peak | Limiting metric |
|---|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---|
| **Silver**   | 2 | 250 m | 1 cpu | 256 Mi | 1 Gi | **414/s** | 373,499 | 82.6 µs | 414/s | **0** | 6.94 % | none — Silver idle for repo |
| **Gold**     | 4 | 500 m | 2 cpu | 512 Mi | 2 Gi | **2,070/s** | 1,867,500 | 71.8 µs | ~502/s | **1.42 M growing** | 10.19 % | **app consumer handler** |
| **Platinum** | 8 | 1 cpu | 4 cpu | 1 Gi  | 4 Gi | **3,238/s** (+ 253 k k6-dropped) | 2,921,163 | 214 µs | ~1,248/s | **1.76 M growing** | 10.94 % | **app consumer handler + k6 producer ceiling** |

## App pod actual consumption (Container Insights)

Per-pod CPU + memory averaged across all replicas during each run window. Source: Azure Log Analytics workspace `aks-test-workspace`, `Perf` table (container `hex-scaffold`).

| Tier | Pods | CPU avg per pod | CPU max (burst) | % of limit avg | Memory avg per pod | Mem % of limit | Pod restarts during run |
|---|---|---:|---:|---:|---:|---:|---:|
| **Silver**   | 2 | **622 m** | 1,778 m | 62 % avg, **178 % burst** | 240 MiB | 24 % | 0 of 2 |
| **Gold**     | 4 | **922 m** | 2,147 m | 46 % avg, **107 % burst** | 162 MiB | 8 %  | 1 of 4 (1 restart) |
| **Platinum** | 8 | **1,547 m** | 4,294 m | 39 % avg, **107 % burst** | 150 MiB | 4 %  | 3 of 8 (1 restart each) |

Restarts likely from liveness probes missing their deadline during CPU bursts — restart frequency tracks tier load. Phase 6 follow-up: relax probe `failureThreshold` or shorten probe period under load.

Pods exceed `requests` substantially (Silver 2.5×, Gold 1.84×, Platinum 1.55×) — the original requests were sized for steady-state minima, not handler peak. Pods burst slightly beyond their CPU limit at peak sample (CFS bursting within the 30 s scrape window) — confirms periodic throttle-ceiling contact, but the average headroom under the limit is large at Gold and Platinum.

## Per-pod throughput efficiency

| Tier | Pods | Total consumed | Per-pod consume rate | Per-pod CPU avg | events/s/mcore |
|---|---|---:|---:|---:|---:|
| Silver  | 2 | 373,499 | **207 events/s/pod** | 622 m | **0.33** |
| Gold    | 4 | ~470,000 | **125 events/s/pod** | 922 m | **0.14** (-58 % vs Silver) |
| Platinum| 8 | ~1,160,000 | **156 events/s/pod** | 1,547 m | **0.10** (-70 % vs Silver) |

Per-mcore throughput **degrades 3.3× from Silver to Platinum** — the consumer handler burns more CPU per event as fetch batches grow and partition contention rises. This points the Phase 6 follow-up at the handler itself (Mediator dispatch / EF Core SaveChanges / per-batch bookkeeping), not at adding more pods or more CPU.

## Per-pod consumer rate (text summary)

The consumer handler tops out around **150–200 events/s/pod**. Beyond Silver, scaling pods adds throughput sub-linearly because each new pod still hits the same per-pod handler ceiling **AND** burns more CPU per event (per-mcore throughput drops from 0.33 at Silver to 0.10 at Platinum — see efficiency table above). The Platinum overlay's aggressive fetch batching (`maxPollRecords=500`, `fetchMinBytes=65536`) recovers ~25 % of per-pod throughput vs Gold but does not eliminate the bottleneck.

## Kafka transport headroom

| Indicator | Silver | Gold | Platinum |
|---|---|---|---|
| `kafka_writer_error_count` | 0 | 0 | 0 |
| `kafka_writer_retries_count` | 0 | 0 | 0 |
| Producer-side write p95 | 82 µs | 72 µs | 214 µs |
| Producer-side write max | 48 ms | 62 ms | 1.08 s |

Brokers handled all three tiers with no errors and microsecond-class producer latency. The Platinum p95 jump (72 µs → 214 µs) tracks the k6-side queueing (VUs exhausted at 720), not broker degradation. Broker `BytesIn / MessagesIn` headroom: massive — never the limit.

## Telemetry coverage

| Lane | Silver | Gold | Platinum |
|---|---|---|---|
| k6 stdout summary | ✅ (salvaged via `kubectl logs` from Completed pod) | ✅ | ✅ |
| Consumer-group lag (live) | ✅ (re-snapshot post-DNS-recovery) | ✅ | ✅ |
| Repo CPU (Azure Monitor) | ⚠️ partial (3 samples, DNS outage extended iterations) | ✅ full series | ✅ full series |
| App Insights `inbound_event_processing_duration_ms` | ❌ DNS outage | ❌ metric not landing | ❌ metric not landing |

The **AI metric pipeline is broken across all 3 tiers**, even on the runs with stable network. Diagnostic queries (`ai-diag-norid.json`, `ai-diag-names.json`, `ai-diag-traffic.json`) captured by driver v3 in gold/platinum runs will tell whether the metric is shipped under a different name, missing the runId tag, or never registered. **Phase 6 follow-up: inspect `ObservabilityConfig.cs` US-004 wiring.**

## Outbound suppression confirmation

`features.outboundAdapter=rest` was active across all 3 runs (verified via helm template render); `NoOpEventPublisher` is the bound `IEventPublisher`. No outbound Kafka topic contamination from these runs.

## Driver evolution

| Driver version | Change | Why |
|---|---|---|
| v1 (Phase 5 first attempt) | baseline | k6 logs lost to delete-before-capture race; AI count=0 |
| v2 (this redo, first try) | added `kubectl cp /tmp/summary.json` BEFORE delete; added 3 AI diagnostic queries | `kubectl cp` failed on Completed pods; DNS outage broke all `az` calls |
| **v3 (current)** | replaced `kubectl cp` with `kubectl logs` (works on Completed pods); wrapped all `az` + `kubectl` calls in `gtimeout` 15–25 s and `--request-timeout` | proven on gold + platinum runs — clean artifact capture, no DNS-hung iterations |

## Side-by-side Kafka-vs-API comparison (note)

The previous (2026-05-04) PG saturation runs measured the **API path** (REST endpoints) and pushed PG into the 60–80% CPU band. Today's Kafka-path runs barely scratched 11% CPU because the bottleneck moved upstream to the application's consumer handler. The Kafka path is **handler-bound**, not DB-bound, at the current per-pod handler throughput. Direct same-tier comparison would require either: (a) scaling the consumer handler horizontally to match API-path concurrency, or (b) measuring the consumer handler's per-event cost and modelling DB saturation forward.

## Stop conditions

| Tier | Cap reached | Repo CPU breach | Lag breach (manual) | k6 producer ceiling |
|---|---|---|---|---|
| Silver  | YES | NO | NO (lag = 0) | NO |
| Gold    | YES | NO | YES (lag → 1.42 M) | NO |
| Platinum| YES | NO | YES (lag → 1.76 M) | YES (253 k dropped iter) |

Driver currently only fires on `cap-reached` / `repo-cpu-breach` / `p95-breach`. Adding a `lag-growth-monotonic` stop rule would have ended Gold and Platinum runs at the moment saturation became evident (saving ~10 min each).

## Phase 6 deliverable status

| Item | Status |
|---|---|
| Per-run reports for 3 PG tiers | ✅ `reports/loadtest-run-2026-05-06-postgresql-{silver,gold,platinum}-kafka.md` |
| Consolidated PG summary | ✅ this file |
| Per-run reports for 3 DocDB tiers | ⏳ blocked on Mongo `dbuser` SCRAM auth |
| Consolidated DB summary (PG vs DocDB, Kafka path) | ⏳ blocked on DocDB runs |
| App Insights p95 column | ❌ blocked on metric-pipeline fix |

## Recommended Phase 6 follow-ups

1. **Fix the App Insights metric pipeline** — without `inbound_event_processing_duration_ms` we cannot report end-to-end p95 for any tier. The diagnostic JSON files captured by driver v3 in Gold/Platinum runs identify whether the metric is registered, named, or tagged differently than the query expects.
2. **Profile the consumer handler** — per-pod ceiling at ~150–200 events/s warrants tracing per stage (Kafka deserialize → Mediator dispatch → EF Core SaveChanges → offset commit). The handler is the limiting tier-comparison metric, not the DB.
3. **Scale k6 producer for Platinum** — `maxVUs=720` exhausts at the offered rate. Either raise to ≥ 1500 or run parallel TestRunners.
4. **Add `lag-growth-monotonic` stop rule** to driver — would end Gold/Platinum runs immediately on lag-saturation instead of running to cap.
5. **Resolve DocDB password issue** — script default rejected. Either confirm intended credentials or rotate the cluster admin password.
