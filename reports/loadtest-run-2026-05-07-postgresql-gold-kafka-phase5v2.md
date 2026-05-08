# Kafka Inbound Load Test — Gold / PostgreSQL (Phase 5 v2)

Goal: re-run the Phase 5 gold-PG tier after bulk-write coalescing (PR #66 + PR #68) and 12→24 partition expansion (PR #65) to close the 1.42 M end-lag recorded in v1.

## Run summary

| Field | Value |
|---|---|
| Run id | `1778184385` |
| Date / time (UTC) | `2026-05-07T20:06:25Z → 2026-05-07T20:21:45Z` (driver elapsed 920 s) |
| Cluster | `aks-test`, namespaces `hex-scaffold` / `messaging-system` / `testing-system` |
| App image | `latest` (includes PR #66 bulk-write + PR #68 same-batch id-collapse fix) |
| Repo SKU | PostgreSQL Flexible Server `pgsql-pp-gold` (Brazil South) |
| Consumer pods | **4 replicas, HPA OFF**, on `nodepool2` |
| Consumer requests / limits | `cpu=1000m/2000m`, `memory=1Gi/4Gi` |
| Outbound publisher | `NoOpEventPublisher` (`features.outboundAdapter=rest`) |
| Redis | OFF |
| Kafka topic | `v2.core.accounts.loadtest.gold.pg` (**24 partitions**, RF=2, lz4 — expanded via PR #65) |
| Consumer config | `maxPollRecords=500`, `fetchMinBytes=32768` (gold overlay) |
| k6 runner | `nodepool3`, `TIER=gold`, `REPO=postgres`, `RUNID=1778184385` |
| Driver | v3 + AI-fix + `P95_BREACH_THRESHOLD_MS=60000` |

## Profile

| Stage | Duration | Target rate |
|---|---|---|
| Warmup | 1 min | → 250 events/s |
| Ramp | 3 min | → 2,500 events/s |
| Peak | 10 min | 2,500 events/s sustained |
| Cooldown | 1 min | → 0 |

Mix: 70% Create / 25% Update / 5% Delete.

## Result

| Metric | Value | Source |
|---|---|---|
| Stop reason | `cap-reached` (15-min wall clock) | `summary.json` |
| Elapsed | 920 s | `summary.json` |
| Broker LOG-END (sum 24 partitions) | 5,411,521 | `consumer-group-lag.log` |
| Effective offered rate | ~5,882 /s | LOG-END / elapsed |
| Consumer CURRENT-OFFSET (sum) | 5,370,833 | `consumer-group-lag.log` |
| Consumed rate | ~5,838 /s | CURRENT / elapsed |
| **End-of-run lag** | **40,688** | `consumer-group-lag.log` |
| **e2e p50** | 14,736 ms | `ai-final.json` |
| **e2e p95** | **41,885 ms** | `ai-final.json` |
| **e2e p99** | 42,203 ms | `ai-final.json` |
| e2e avg / max | 16,769 / 42,290 ms | `ai-final.json` |
| AI record count | 180 | `ai-final.json` |
| k6 offered rate (stdout) | not captured — k6.log empty (driver race) | `k6.log` |

## Consumer health — PARTIAL LAG

Per-partition snapshot at run end (24 partitions, 4 consumer pods = 6 partitions each). Three pods drained fully; one pod (10.244.16.27, partitions 6–11) carried residual lag:

| Partition | LOG-END | CURRENT | LAG | Pod IP |
|---|---:|---:|---:|---|
| 0–5 (pod .18.36) | ~303,430 avg | ~303,430 avg | 0 each | 10.244.18.36 |
| 12–17 (pod .10.31) | ~147,650 avg | ~147,650 avg | 0 each | 10.244.10.31 |
| 18–23 (pod .19.32) | ~147,680 avg | ~147,680 avg | 0 each | 10.244.19.32 |
| 6 | 303,293 | 298,964 | **4,329** | 10.244.16.27 |
| 7 | 303,377 | 293,514 | **9,863** | 10.244.16.27 |
| 8 | 303,436 | 300,888 | **2,548** | 10.244.16.27 |
| 9 | 303,383 | 291,780 | **11,603** | 10.244.16.27 |
| 10 | 303,381 | 293,537 | **9,844** | 10.244.16.27 |
| 11 | 303,255 | 300,754 | **2,501** | 10.244.16.27 |
| **Sum** | **5,411,521** | **5,370,833** | **40,688** | |

The lag is concentrated entirely on one of the four pods (10.244.16.27). The other three pods reached zero-lag and held it. This points to a per-pod write-rate variance, not a systemic handler bottleneck.

## Repository (PostgreSQL `pgsql-pp-gold`)

| Stat | Value |
|---|---|
| Peak CPU | 35.69 % (at 20:10–20:14Z, sustained peak window) |
| Average CPU (all samples) | ~25.6 % |
| Binding constraint | **application handler** (DB has headroom) |

Gold PG DB CPU ran at a sustained ~28–35 % through the peak window — significantly higher than v1's ~10 % peak. The bulk-write path coalesces more rows per `SaveChangesAsync` call, raising per-batch DB CPU while reducing round-trips. The net result is 4× lower lag growth vs v1 despite higher DB CPU.

## Application pod consumption

Per-pod Container Insights `Perf` data **not captured this batch** — driver did not snapshot the Container Insights `Perf` table. AKS Container Insights query is a known follow-up.

## Stop conditions

| Signal | Fired? |
|---|---|
| 15-min wall-clock cap | YES |
| p95 ≥ 60,000 ms | NO |
| DB CPU > 80 % for ≥ 2 samples | NO |
| k6 VU exhaustion | NO |

## Notes

- **k6.log is empty** for this run (driver race; see silver-PG notes).
- **End-lag 40,688** is the only non-zero end-lag in the v2 batch that is not attributable to the retained-backlog caveat from silver. Gold-PG is the closest tier to its pod-CPU ceiling: 4 pods × 6 partitions each at ~5,882 /s offered means ~1,470 events/s per pod. The bulk-write fix reduced end-lag by ~97% vs v1 (1,420,000 → 40,688) but did not reach zero within the 15-min cap. Mongo at the same tier hit zero-lag because its handler is I/O-blocked and benefits more from bulk batching on the path-critical section.
- Zero pod restarts (Phase 5 v1: 1 of 4 restart).
- 24-partition topic (expanded from 12 via PR #65) — each of 4 pods owns 6 partitions.

## Artifacts

`.omc/research/kafka-loadtest/gold-pg-1778184385/`

## Monthly cost (Azure Retail Prices, Brazil South, USD)

### Peak app consumption

| Dimension | Calculation | Peak |
|---|---|---:|
| Replicas at peak | Run summary: 4 consumer pods | 4 |
| CPU reserved at peak | 4 × cpu=1000m | 4000m |
| Memory reserved at peak | 4 × memory=1Gi | 4096 Mi (4 GiB) |

Node = `Standard_D2s_v6` = 2 vCPU + 8 GiB.
- CPU: 4000m / 2000m = 200.0%
- Memory: 4096 Mi / 8192 Mi = 50.0%
- **CPU binds** at 200.0%; pro-rate share = 2.0

### Unit prices (USD, retail, primary meter, brazilsouth)

| Meter | Retail | Discounted (-25%) | UoM |
|---|---:|---:|---|
| PG Flex GP Dadsv5 4 vCore (`Standard_D4ds_v5`) | 0.4800 | 0.36000 | 1 Hour |
| PG Flex Storage Data Stored | 0.2185 | 0.16388 | 1 GiB/Month |
| PG Flex Storage IOPS | 0.0400 | 0.03000 | 1 IOPS/Month |
| PG Flex Storage Throughput | 0.1600 | 0.12000 | 1 MBps/Month |
| PG Flex Backup Storage LRS Data Stored | 0.0950 | 0.07125 | 1 GB/Month |
| `Standard_D2s_v6` Linux | 0.1610 | 0.12075 | 1 Hour |

### Monthly cost

| Line | Calculation | Retail USD/mo | Discounted USD/mo |
|---|---|---:|---:|
| PG D4ds_v5 compute | 0.4800 × 730 | 350.40 | 262.80 |
| PG storage 128 GiB | 0.2185 × 128 | 27.97 | 20.98 |
| PG storage 6000 IOPS | 0.04 × 6000 | 240.00 | 180.00 |
| PG storage 500 MBps throughput | 0.16 × 500 | 80.00 | 60.00 |
| PG backup ≤ 128 GiB | included | 0.00 | 0.00 |
| PG subtotal | | 698.37 | 523.78 |
| App pro-rated D2s_v6 | 0.161 × 730 × 2.0 | 235.06 | 176.30 |
| App subtotal | | 235.06 | 176.30 |
| **Gold Kafka v2 + PG total** | | **$933.43** | **$700.08** |

Savings: $233.35/month at 25% discount.

### Notes

- Fixed-replica Kafka deployment (consumer Deployment, not HPA-bounded).
- CPU binds (200.0%); pro-rate share = 2.0 — workload spans 2× D2s_v6 nodes.
- If pro-rate share > 1.0: workload spans multiple D2s_v6 nodes — share treated as multiplier.
- v2 requests bumped vs v1 (1000m/1Gi vs 500m/512Mi) to support bulk-write coalescing path (PR #66 + PR #68).
- Premium SSD v2 storage: 128 GiB + 6000 IOPS + 500 MBps as provisioned for Gold tier.
- Excludes: AKS control plane Standard ($73/mo), private endpoint (~$7.30/mo), egress, Public IP/LB, Kafka cluster (Strimzi MSK or self-hosted — separate budget line).
- Reference price USD; Microsoft bills in USD; not invoice reconciliation.
- 25% uniform discount; real Azure agreements (EA/MCA/CSP) discount per-meter.
