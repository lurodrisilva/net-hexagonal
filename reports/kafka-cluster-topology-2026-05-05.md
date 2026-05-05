# Kafka Cluster Topology — Loadtest Cycle (2026-05-05)

Plan reference: `.omc/plans/kafka-loadtest-plan.md` §5 (revision 4).
Cluster name: `hex-scaffold-loadtest`.
Namespace: `messaging-system` (Strimzi cluster operator already runs here).
Target nodepool: `nodepool3` (5 × 7820m CPU × ~30 GiB RAM allocatable).

This document is **frozen for the cycle** — once Phase 5 runs start, no
broker / NodePool / topic shape changes until Phase 6 cleanup.

---

## Cluster shape

| Field | Value |
|---|---|
| Mode | KRaft (no ZooKeeper) |
| Strimzi annotations | `strimzi.io/kraft: enabled`, `strimzi.io/node-pools: enabled` |
| Kafka version | 3.8.0 |
| NodePool name | `hex-scaffold-loadtest-pool` |
| Nodes | 3 |
| Roles | combined `controller,broker` per node |
| Per-node CPU | 1500m request / 2000m limit |
| Per-node RAM | 4 GiB request / 6 GiB limit |
| Per-node storage | 50 GiB persistent-claim, `managed-csi` (Azure Disk CSI, WaitForFirstConsumer) |
| Listener | `plain` :9092 internal, no TLS, no auth (loadtest-only) |
| `default.replication.factor` | 3 |
| `min.insync.replicas` | 2 |
| `offsets.topic.replication.factor` | 3 |
| `transaction.state.log.replication.factor` | 3 |
| `transaction.state.log.min.isr` | 2 |
| `compression.type` (broker) | `producer` (defers to topic-level lz4) |

### Cruise Control

| Field | Value |
|---|---|
| `brokerCapacity.cpu` | `2000m` (mirrors NodePool CPU limit) |
| `brokerCapacity.inboundNetwork` | `10000KB/s` |
| `brokerCapacity.outboundNetwork` | `10000KB/s` |
| Container resources | 200m / 512Mi requests, 500m / 1Gi limits |

### Sizing rationale

Plan §2.2 calls for the Kafka transport "sized one tier above the highest
test rate so it never bounds the application." The headline ceiling for
Phase 5 is **Platinum @ ~5,300 events/s** (PG, per the existing 2026-05-04
API ladder). At 3 brokers × 1.5 vCPU = 4.5 vCPU of broker capacity vs an
expected steady-state broker CPU < 1 vCPU per broker at ~1,800 events/s
each, the headroom is ~3×. RAM is sized for PageCache headroom (~2 GiB
working set + JVM heap of ~1.5 GiB fits inside the 4 GiB request).

Storage at 50 GiB per broker × 3 brokers = 150 GiB total, which holds
1 hour of retention at Platinum lz4-compressed throughput (~30 GiB/h
across 12 partitions × 6 topics × RF 3, comfortably under cap).

---

## Loadtest topics (six)

| Topic name | Partitions | RF | Retention | Compression | Segment bytes |
|---|---|---|---|---|---|
| `v2.core.accounts.loadtest.silver.pg`     | 12 | 3 | 1 h | lz4 | 512 MB |
| `v2.core.accounts.loadtest.silver.mongo`  | 12 | 3 | 1 h | lz4 | 512 MB |
| `v2.core.accounts.loadtest.gold.pg`       | 12 | 3 | 1 h | lz4 | 512 MB |
| `v2.core.accounts.loadtest.gold.mongo`    | 12 | 3 | 1 h | lz4 | 512 MB |
| `v2.core.accounts.loadtest.platinum.pg`   | 12 | 3 | 1 h | lz4 | 512 MB |
| `v2.core.accounts.loadtest.platinum.mongo`| 12 | 3 | 1 h | lz4 | 512 MB |

Partition count (12) chosen as 4× the broker count so the consumer group
at Platinum (8 pods) still has spare partitions for parallelism. **This
deviates from plan §2.2's tier-stepped 12/18/24 partition counts** — the
PRD collapsed to uniform 12 for operator simplicity (one partition shape
across all six topics). Trade-off: at Platinum (8 consumer pods × 12
partitions = 1.5 partitions/pod average), Kafka's "at most 1 partition
per consumer per group" rule means **4 of the 8 pods will sit idle**
during steady-state. Acceptable for this cycle — the wall-clock target
(200 ms p95) is dominated by per-message handler cost, not consumer
parallelism. If a future cycle pushes past Platinum's ceiling, bump
platinum topics to 24 partitions.

Additionally, **observability is wired at the cluster level** (see Q2/Q3
of `tests/loadtest/grafana/kafka-loadtest.json`) via:
- `spec.kafka.metricsConfig` → JMX Prometheus exporter sidecar (drives
  `kafka_server_*` metrics for Q2 BytesInPerSec / RequestHandlerAvgIdle /
  UnderReplicatedPartitions panels), backed by ConfigMap
  `hex-scaffold-loadtest-jmx-config` shipped in the same multi-document
  YAML as the Kafka CR.
- `spec.kafkaExporter` → emits `kafka_consumergroup_lag` per-partition,
  the primary stop-condition signal for Q3 (lag panel) and the plan §4.3
  "stop if monotonic >2 min" rule. Bounded to loadtest topics + groups
  via `topicRegex` / `groupRegex`.

### Consumer-group ID convention

`hex-scaffold-loadtest-{tier}-{repo}-{runId}` — established in plan §6.1
and consumed by the k6 producer headers in Phase 4. The `{runId}` suffix
is the 8-digit timestamp captured at run start so consecutive runs of the
same tier × repo never share offset commits.

Per plan §7.2 pre-flight, the prior consumer group is deleted before each
run via `kafka-consumer-groups.sh --delete --group hex-scaffold-loadtest-<tier>-<repo>-*`.

---

## Apply order — Phase 5 pre-flight

```bash
# 1. Apply the cluster + NodePool. Wait for Ready (~3 min).
kubectl apply -f tests/loadtest/kafka/kafka-cluster.yaml
kubectl wait --for=condition=Ready -n messaging-system kafka/hex-scaffold-loadtest --timeout=10m

# 2. Apply the six KafkaTopics. Wait for Ready (each ~10 s).
kubectl apply -f tests/loadtest/kafka/kafkatopics-loadtest.yaml
for tier in silver gold platinum; do
  for repo in pg mongo; do
    kubectl wait --for=condition=Ready -n messaging-system \
      kafkatopic/v2-core-accounts-loadtest-${tier}-${repo} --timeout=2m
  done
done

# 3. (Optional) Verify topic state.
kubectl exec -n messaging-system hex-scaffold-loadtest-pool-0 -c kafka -- \
  bin/kafka-topics.sh --bootstrap-server localhost:9092 --list | grep loadtest

# 4. Verify the JMX exporter is exposing the COUNTER metrics the Q2
#    dashboard panels query. Returns one line per broker if wired correctly.
kubectl exec -n messaging-system hex-scaffold-loadtest-pool-0 -c kafka -- \
  curl -s localhost:9404/metrics | grep '^kafka_server_brokertopicmetrics_bytesin_total'

# 5. Verify the Prometheus scrape adds the kafka_broker_id label that the
#    Q2 panels' `sum by (kafka_broker_id)` clause depends on. If absent,
#    edit the dashboard `sum by` clause to use `pod` instead — 30 s fix
#    vs 30 min mid-cycle. Same applies to the strimzi_io_cluster label
#    used in the panel filters.
kubectl get podmonitor -n messaging-system \
  -l strimzi.io/cluster=hex-scaffold-loadtest -o yaml | grep -E 'kafka_broker_id|strimzi_io_cluster'
```

---

## Cleanup — Phase 6

```bash
# Drop the topics first so the Topic Operator removes the retention
# segments cleanly before the cluster goes away.
kubectl delete -f tests/loadtest/kafka/kafkatopics-loadtest.yaml

# Wait briefly for the operator to drain the topic deletes.
sleep 30

# Tear down the cluster + NodePool. PVCs are auto-deleted because the
# NodePool spec sets storage.deleteClaim: true.
kubectl delete -f tests/loadtest/kafka/kafka-cluster.yaml
```

If post-cycle troubleshooting later wants to inspect the broker logs,
add `kubectl logs -n messaging-system hex-scaffold-loadtest-pool-{0,1,2} -c kafka >
broker-N.log` BEFORE running the cluster delete.

---

## Phase 3 → Phase 4 / 5 handoff

Phase 4 (k6 producer + TestRun manifests) consumes:

- The six topic names above (k6 script's `TOPIC` derives `${tier}.${repo === "postgres" ? "pg" : "mongo"}`).
- The bootstrap address: `hex-scaffold-loadtest-kafka-bootstrap.messaging-system.svc:9092` (Strimzi-rendered service name follows `<cluster>-kafka-bootstrap.<ns>.svc`).
- The consumer-group naming scheme above (used by the plan §7.2 pre-flight delete + by Q3 of the Grafana dashboard at `tests/loadtest/grafana/kafka-loadtest.json`).

Phase 5 install command flips `--set kafka.inboundTopic=v2.core.accounts.loadtest.<tier>.<short-repo>`
to wire the application's consumer to the per-run topic.
