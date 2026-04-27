# Load Testing Guide

This project scaffold is designed to be **load-tested end-to-end** against either inbound adapter (REST or Kafka) with Postgres **or** Mongo (optionally + Redis) behind it. Every knob is driven by the Helm chart's ConfigMap — you deploy a chart instance per test target, drive load at it, and inspect telemetry in Application Insights.

> **Prerequisites**
> - Kubernetes ≥ 1.27
> - [Strimzi Kafka Operator](https://github.com/strimzi/strimzi-kafka-operator) (only for Kafka tests)
> - [Grafana k6 Operator](https://github.com/grafana/k6-operator) (only for REST tests)
> - `kubectl`, `helm`, and (for local runs) a stand-alone `k6` binary

---

## 1. Deploy the scaffold

```bash
# Pick ONE inbound + ONE outbound + ONE persistence backend via --set.
helm upgrade --install hex-scaffold ./deploy/helm/hex-scaffold \
  --namespace default --create-namespace \
  --set features.inbound=rest \
  --set features.outbound=kafka \
  --set features.persistence=postgres \
  --set features.redis=true \
  --set secrets.appInsightsConnectionString="$APP_INSIGHTS_CS"
```

The chart:
- Renders a ConfigMap with `Features__*` keys; the application reads them into `FeaturesOptions` at startup.
- Runs an EF Core migration Job as a `pre-install` / `pre-upgrade` hook when `persistence=postgres`.
- Wires Application Insights when `secrets.appInsightsConnectionString` is non-empty.

### Validate the rollout
```bash
kubectl get pods,svc,configmap -l app.kubernetes.io/instance=hex-scaffold
kubectl logs -l app.kubernetes.io/instance=hex-scaffold --tail=100
# Look for: "Features: Inbound=rest, Outbound=kafka, Persistence=postgres, UseRedis=True"
```

---

## 2. REST API load test (k6 Operator)

The k6 script lives at [`tests/loadtest/k6/rest-api-loadtest.js`](../tests/loadtest/k6/rest-api-loadtest.js) and covers the four endpoints exposed by the Stripe v2 Accounts API surface:

```
POST /v2/core/accounts          create
GET  /v2/core/accounts/{id}     retrieve
POST /v2/core/accounts/{id}     update (partial; Stripe uses POST)
GET  /v2/core/accounts          list (cursor-paginated)
```

The CRUD scenario is `POST → GET → POST(update)` (no DELETE — `Account.close` is not surfaced as an endpoint; out of scope). A read-heavy `list_heat` scenario hits the first cursor page repeatedly to keep the read path warm. Per-request thresholds are tied to the **four golden signals**:

| Signal | k6 metric |
|--------|-----------|
| **Latency** | `http_req_duration{name:*}` p95 / p99 |
| **Traffic** | `golden_traffic_total_requests` |
| **Errors** | `http_req_failed` + `golden_errors_rate` |
| **Saturation** | `golden_saturation_concurrent_vus` |

### 2.1 Run in-cluster with the k6 Operator (recommended)

```bash
# 1. Install the operator if you haven't already
kubectl apply -k "github.com/grafana/k6-operator/config/default?ref=main"

# 2. Run end-to-end via the orchestration script (apply → tail logs → wait → cleanup)
./tests/loadtest/k6/loadtest.sh run

# Or step-by-step:
./tests/loadtest/k6/loadtest.sh prereq   # verify CRDs + service reachability
./tests/loadtest/k6/loadtest.sh apply    # ConfigMap + TestRun
./tests/loadtest/k6/loadtest.sh logs     # tail runner pods (Ctrl-C to detach)
./tests/loadtest/k6/loadtest.sh wait     # block until stage=finished/error
./tests/loadtest/k6/loadtest.sh summary  # extract the textSummary block
./tests/loadtest/k6/loadtest.sh cleanup  # delete TestRun + ConfigMap
```

Tunables: `NAMESPACE`, `BASE_URL`, `PARALLELISM`, `TIMEOUT`, `TESTRUN_NAME`, `CONFIGMAP_NAME` — environment variables documented in the script's help (`./loadtest.sh help`).

> **Bump the rate limiter first.** The chart defaults to `permitLimit: 100, windowSeconds: 60` per IP. The k6 ramped profile peaks at 150 req/s, so without a higher cap the runners will be 429-throttled and `golden_throttled_rate` will dominate the run. `helm upgrade --set rateLimit.permitLimit=100000` before running.

### 2.2 Run locally

```bash
BASE_URL=http://localhost:8080 k6 run tests/loadtest/k6/rest-api-loadtest.js
# Or flat-profile smoke:
VUS=20 DURATION=1m BASE_URL=http://localhost:8080 \
  k6 run tests/loadtest/k6/rest-api-loadtest.js
```

### 2.3 Ship metrics directly to Azure Monitor Prometheus

Uncomment the Prometheus remote_write arguments in `testrun.yaml`, then set the endpoint through env in the k6 Operator's runner (see the [k6 Prometheus output docs](https://grafana.com/docs/k6/latest/results-output/real-time/prometheus-remote-write/)).

---

## 3. Kafka load test (Strimzi + kafka-cli)

> **Status note.** The Kafka driver at [`tests/loadtest/kafka/load-test.sh`](../tests/loadtest/kafka/load-test.sh) and the committed event deck at [`tests/loadtest/kafka/sample-events.jsonl`](../tests/loadtest/kafka/sample-events.jsonl) still target the prior **Sample** aggregate, which has been retired in favor of the **Account** aggregate. The driver hasn't been migrated yet — the inbound `AccountEventConsumer` is intentionally minimal (log + commit) and doesn't drive a read-model the way the prior `SampleEventConsumer` did, so the Kafka path needs a fresh design before a new event deck makes sense. Tracked as a follow-up.

The remainder of this section describes the historical setup so the existing kafka tooling stays understandable. The driver follows the AWS data-on-eks pattern: it runs inside a Strimzi Kafka image (`quay.io/strimzi/kafka:*`) that ships every CLI you need (`kafka-topics.sh`, `kafka-console-producer.sh`, `kafka-producer-perf-test.sh`).

### 3.1 Sample event deck (legacy)

[`sample-events.jsonl`](../tests/loadtest/kafka/sample-events.jsonl) contains **25 committed events** exercising the prior Sample aggregate's event vocabulary (`SampleCreatedEvent` / `SampleUpdatedEvent` / `SampleDeletedEvent`). It is **not consumed by the current `AccountEventConsumer`** — left in the tree for reference until the Kafka path is migrated.

### 3.2 Run in-cluster (Strimzi)

```bash
# 1. Install Strimzi if you haven't already
kubectl create namespace kafka
kubectl apply -f 'https://strimzi.io/install/latest?namespace=kafka' -n kafka

# 2. Bring up a small cluster (see Strimzi examples)
kubectl apply -n kafka -f https://strimzi.io/examples/latest/kafka/kafka-single-node.yaml

# 3. Bundle the script + sample events into a ConfigMap
kubectl -n kafka create configmap hex-scaffold-kafka-loadtest \
  --from-file=load-test.sh=tests/loadtest/kafka/load-test.sh \
  --from-file=sample-events.jsonl=tests/loadtest/kafka/sample-events.jsonl

# 4. Create the topic (one-shot)
kubectl -n kafka apply -f tests/loadtest/kafka/strimzi-producer-job.yaml  # edit args to ["create-topic"]

# 5. Seed committed events
#    (edit the Job's args to ["seed"] and re-apply)
kubectl -n kafka logs job/hex-scaffold-kafka-loadtest -f

# 6. Drive synthetic volume (80% create / 15% update / 5% delete)
#    (edit args to ["synthetic"], set NUM_EVENTS and THROUGHPUT)
kubectl -n kafka apply -f tests/loadtest/kafka/strimzi-producer-job.yaml
```

### 3.3 Subcommands

```bash
load-test.sh create-topic   # create `sample-events` with PARTITIONS / REPLICATION_FACTOR
load-test.sh seed           # produce every committed event once
load-test.sh synthetic      # produce NUM_EVENTS mixed events at THROUGHPUT msg/s
load-test.sh perf           # kafka-producer-perf-test for raw throughput
```

Environment knobs: `BOOTSTRAP_SERVERS`, `TOPIC`, `PARTITIONS`, `REPLICATION_FACTOR`, `NUM_EVENTS`, `THROUGHPUT`, `RECORD_SIZE`, `SEED_FILE`.

---

## 4. Validating results in Application Insights

All signals flow through the OpenTelemetry pipeline configured in [`ObservabilityConfig.cs`](../src/Hex.Scaffold.Api/Configurations/ObservabilityConfig.cs). When `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, the Azure Monitor exporter attaches to the existing TracerProvider / MeterProvider / LoggerProvider, so the same deck powers both local OTLP collectors and Azure Monitor.

| Azure Monitor view | Shows |
|--------------------|-------|
| **Application Map** | `/v2/core/accounts` RPS + dependency edges to Postgres / Kafka (Mongo unused at present — see `docs/adapters.md`) |
| **Live Metrics** | real-time RPS, failure rate, duration |
| **Failures** | 4xx/5xx from `requests` + tracked exceptions |
| **Performance** | latency percentiles per operation |
| **Metrics → customMetrics** | `http.server.request.duration`, `kestrel.connections.active`, `process.runtime.dotnet.gc.*` |
| **Logs (KQL)** | `requests`, `dependencies`, `traces`, `exceptions`, `customMetrics` |

Recommended KQL for the four golden signals:

```kusto
// Latency p95 per route
requests
| where timestamp > ago(30m)
| summarize p95 = percentile(duration, 95) by name, bin(timestamp, 1m)

// Traffic
requests | where timestamp > ago(30m)
| summarize rps = count() / 60.0 by bin(timestamp, 1m)

// Errors
requests | where timestamp > ago(30m)
| summarize fail_rate = countif(success == false) * 1.0 / count() by bin(timestamp, 1m)

// Saturation (runtime)
customMetrics
| where name in ("process.runtime.dotnet.thread_pool.threads.count",
                 "process.runtime.dotnet.gc.heap.size")
| summarize avg(value) by name, bin(timestamp, 1m)
```

---

## 5. Tearing down

```bash
kubectl delete testrun hex-scaffold-rest-loadtest
kubectl -n kafka delete job hex-scaffold-kafka-loadtest
helm uninstall hex-scaffold
```
