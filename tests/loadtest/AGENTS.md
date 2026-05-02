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
- `runner.nodeSelector` pins runners to a dedicated nodepool. AKS labels nodes with `agentpool=<name>` automatically. Capacity math: each `Standard_D2s_v6` node has ~1370m CPU allocatable after system DaemonSets, so `runner.requests.cpu` × `parallelism` per node must fit. If runners stick `Pending` with "insufficient cpu", lower the request or scale the nodepool.
- `loadtest-pgsql-pp.sh apply` re-renders and re-applies the TestRun. The k6-operator only re-reads `spec.parallelism` on apply, not on its own polling.
- `loadtest-pgsql-pp.sh summary` greps the textSummary block from runner logs — the awk start pattern must match the banner string in the JS exactly (`hex-scaffold v2 Accounts load-test summary`).
- The `kafka/` Job uses Strimzi's `kafka-producer-perf-test.sh` image to push `sample-events.jsonl` into the input topic. Tune throughput via `--throughput`, total record count via `--num-records`.

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
