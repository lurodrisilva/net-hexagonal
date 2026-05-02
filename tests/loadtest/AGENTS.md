<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# loadtest

## Purpose
Cluster-side load tests. Two flavors: a k6-Operator REST scenario (`k6/`) hitting the four `/v2/core/accounts` endpoints, and a Strimzi-job-based Kafka producer (`kafka/`) for inbound-Kafka path soak tests. Designed to run in-cluster against a real Helm-deployed `hex-scaffold`.

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `k6/` | k6 script + `TestRun` manifest + orchestration shell wrapper. Spec in `rest-api-loadtest-pgsql-pp.js`, manifest in `testrun-pgsql-pp.yaml`, wrapper in `loadtest-pgsql-pp.sh` |
| `kafka/` | Strimzi `Job` producing JSONL events from `sample-events.jsonl` for the inbound Kafka adapter |

## For AI Agents

### Working In This Directory
- The k6 script emits both per-endpoint metrics (`http_req_duration{name:create|get|list|update}`) and four "golden signal" custom metrics (`golden_latency_*`, `golden_traffic_total_requests`, `golden_errors_rate`, `golden_throttled_rate`, `golden_saturation_concurrent_vus`). Thresholds are calibrated for the chart's default resource shape; don't bump thresholds without checking the deployed pod CPU limits first.
- 429 responses are tracked in `golden_throttled_rate`, NOT `golden_errors_rate` — the rate limiter doing its job is expected backpressure under peak, not a system fault.
- `runner.nodeSelector` pins runners to a dedicated nodepool. AKS labels nodes with `agentpool=<name>` automatically. See "Current State (2026-05-02)" below for nodepool capacity details.
- `loadtest-pgsql-pp.sh apply` re-renders and re-applies the TestRun. The k6-operator only re-reads `spec.parallelism` on apply, not on its own polling.
- `loadtest-pgsql-pp.sh summary` greps the textSummary block from runner logs — the awk start pattern must match the banner string in the JS exactly (`hex-scaffold v2 Accounts load-test summary`).
- The `kafka/` Job uses Strimzi's `kafka-producer-perf-test.sh` image to push `sample-events.jsonl` into the input topic. Tune throughput via `--throughput`, total record count via `--num-records`.

### Current State (2026-05-02)

**AKS nodepool composition:**
- `nodepool3` (loadtest target): `Standard_D8s_v6`, 5 nodes, ~7820m CPU allocatable per node (~29 GiB memory per node) — **upgraded from D2s_v6**
- `nodepool2`: `Standard_D2s_v6`, 10 nodes, ~1900m CPU allocatable per node
- `nodepool` (System): `Standard_D2s_v3`, 10 nodes, ~1900m CPU allocatable per node

**The CPU-fit gotcha is now historical.** When nodepool3 was `Standard_D2s_v6` (~1370m), the math meant `runner.requests.cpu=600m × parallelism=6` barely fit (~3.6 GiB per runner × 6 = 21.6 GiB). Commits `48ffb55` and `d0fd2ae` were fixes for that constraint. With D8s_v6, ~13 runners fit per node; the CPU limit no longer binds at PARALLELISM=6.

**Current threshold breach pattern:** The most recent test run (2026-05-02 15:57Z, `hex-scaffold-rest-loadtest`, all 6 runners finished with errors) showed:
- Aggregate ~1,150 RPS reached (191.82 RPS per runner × 6)
- Zero HTTP errors, zero 429s, zero failed requests
- **Latency p95 catastrophically over thresholds:** create=2883ms, get=2719ms, list=2818ms, update=3704ms (thresholds 400/200/300/400ms)
- "Insufficient VUs" warning at 367 active VUs
- **Bottleneck: application-tier CPU starvation, NOT storage** — `pgsql-pp-platinum-1` peaked at ~12% disk_iops_consumed_percentage
- Deployed hex-scaffold: 10 replicas, `requests.cpu=80m / limits.cpu=360m` per pod = ~3.6 vCPU total fleet

**Before raising load further:** Scale hex-scaffold's CPU limits (currently 360m) and add an HPA, or threshold-breach pattern will persist.

### Testing Requirements
- Cluster prerequisites: k6-operator installed in `testing-system` namespace, `hex-scaffold` Helm release deployed in target namespace, `agentpool=<your-loadtest-pool>` nodes available.
- Smoke test: `./loadtest-pgsql-pp.sh prereq` — verifies the cluster has everything before applying anything.

### Common Patterns
- Bash style: `set -euo pipefail`, env-var defaults at top, snake_case subcommand functions, ISO-8601 timestamped log lines via the `log()` helper.
- The k6 script reads `BASE_URL` from env so it works locally (`k6 run …`) and in-cluster (operator injects it).

## Dependencies

### External
- `grafana/k6` runner image (k6-operator pulls `grafana/k6:latest` for runners by default)
- `ghcr.io/grafana/k6-operator` (operator + initializer + starter)
- Strimzi Kafka operator (for the Kafka producer job)
- `kubectl`, `bash`, `curl`, optional `jq`

<!-- MANUAL: -->
