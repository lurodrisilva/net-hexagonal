#!/usr/bin/env bash
# =============================================================================
# hex-scaffold — Kafka inbound-adapter load test
#
# Produces synthetic `sample-events` to the topic the application consumes
# (SampleCreated / SampleUpdated / SampleDeleted). Designed to be executed
# from a Pod running the `quay.io/strimzi/kafka:latest-kafka-3.x` image
# inside a cluster managed by the Strimzi Kafka operator.
#
# Reference pattern (AWS data-on-eks):
#   https://raw.githubusercontent.com/awslabs/data-on-eks/refs/heads/main/data-stacks/kafka-on-eks/examples/load-test.sh
#
# Usage
#   load-test.sh [SUBCOMMAND]
# Subcommands
#   create-topic       Create the `sample-events` topic (partitions/replicas from env)
#   seed               Produce the committed sample deck (sample-events.jsonl) once
#   synthetic          Produce N synthetic events of configurable mix
#   perf               kafka-producer-perf-test drive for raw throughput
#
# Environment (with defaults)
#   BOOTSTRAP_SERVERS=kafka-bootstrap.kafka.svc:9092
#   TOPIC=sample-events
#   PARTITIONS=12           # match or exceed downstream parallelism
#   REPLICATION_FACTOR=3
#   NUM_EVENTS=10000        # synthetic + perf
#   THROUGHPUT=1000         # msg/sec for perf (use -1 to uncap)
#   RECORD_SIZE=512         # perf-only; bytes per record
#   SEED_FILE=/data/sample-events.jsonl
# =============================================================================
set -euo pipefail

BOOTSTRAP_SERVERS="${BOOTSTRAP_SERVERS:-kafka-bootstrap.kafka.svc:9092}"
TOPIC="${TOPIC:-sample-events}"
PARTITIONS="${PARTITIONS:-12}"
REPLICATION_FACTOR="${REPLICATION_FACTOR:-3}"
NUM_EVENTS="${NUM_EVENTS:-10000}"
THROUGHPUT="${THROUGHPUT:-1000}"
RECORD_SIZE="${RECORD_SIZE:-512}"
SEED_FILE="${SEED_FILE:-/data/sample-events.jsonl}"

log() { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

# ---- subcommands ------------------------------------------------------------

create_topic() {
  log "Creating topic '${TOPIC}' (partitions=${PARTITIONS}, rf=${REPLICATION_FACTOR})"
  kafka-topics.sh --bootstrap-server "${BOOTSTRAP_SERVERS}" \
    --create --if-not-exists \
    --topic "${TOPIC}" \
    --partitions "${PARTITIONS}" \
    --replication-factor "${REPLICATION_FACTOR}" \
    --config retention.ms=86400000 \
    --config cleanup.policy=delete
  kafka-topics.sh --bootstrap-server "${BOOTSTRAP_SERVERS}" --describe --topic "${TOPIC}"
}

# Seed: produce every line of sample-events.jsonl exactly once. Each line is
# a JSON object whose `__key` field carries the Kafka message key (event type);
# the rest of the object is the value. The consumer dispatches on the key.
seed() {
  if [[ ! -f "${SEED_FILE}" ]]; then
    log "ERROR: seed file not found at ${SEED_FILE}" >&2
    exit 1
  fi
  local count
  count="$(wc -l < "${SEED_FILE}")"
  log "Seeding ${count} events from ${SEED_FILE} into ${TOPIC}"

  # Split into key <TAB> value, then feed into kafka-console-producer.
  awk -F'"__key":"' '{
      # Extract the key then strip the "__key" field from the JSON value.
      split($2, a, "\"");
      key=a[1];
      sub(/"__key":"[^"]+",/, "", $0);
      printf "%s\t%s\n", key, $0;
    }' "${SEED_FILE}" | \
  kafka-console-producer.sh \
    --bootstrap-server "${BOOTSTRAP_SERVERS}" \
    --topic "${TOPIC}" \
    --property "parse.key=true" \
    --property "key.separator=	" \
    --producer-property "acks=all" \
    --producer-property "enable.idempotence=true"

  log "Seed complete (${count} events)"
}

# Synthetic: produce N events with a realistic mix (80% Created, 15% Updated,
# 5% Deleted). Id space is sequential starting at RANDOM*1e6 to avoid
# colliding with seed events.
synthetic() {
  log "Producing ${NUM_EVENTS} synthetic events -> ${TOPIC} (~${THROUGHPUT} msg/s target)"
  local base_id=$(( (RANDOM % 900) * 1000 + 100000 ))
  local sleep_per=$(awk -v r="${THROUGHPUT}" 'BEGIN{ if (r<=0) print 0; else printf "%.6f", 1/r }')

  awk -v n="${NUM_EVENTS}" -v base="${base_id}" '
    BEGIN {
      srand();
      for (i = 0; i < n; i++) {
        r = rand();
        id = base + (i % 50000);
        if (r < 0.80) {
          status = (rand() < 0.90 ? "Active" : (rand() < 0.5 ? "Inactive" : "NotSet"));
          name   = "synthetic-" id "-" int(rand()*99999);
          printf "SampleCreatedEvent\t{\"Sample\":{\"Id\":{\"Value\":%d},\"Name\":\"%s\",\"Status\":{\"Name\":\"%s\"},\"Description\":\"synthetic load event %d\"}}\n", id, name, status, i;
        } else if (r < 0.95) {
          printf "SampleUpdatedEvent\t{\"Sample\":{\"Id\":{\"Value\":%d},\"Name\":\"updated-%d\",\"Status\":{\"Name\":\"Active\"},\"Description\":\"synthetic update %d\"}}\n", id, id, i;
        } else {
          printf "SampleDeletedEvent\t{\"SampleId\":{\"Value\":%d}}\n", id;
        }
      }
    }
  ' | \
  kafka-console-producer.sh \
    --bootstrap-server "${BOOTSTRAP_SERVERS}" \
    --topic "${TOPIC}" \
    --property "parse.key=true" \
    --property "key.separator=	" \
    --producer-property "linger.ms=20" \
    --producer-property "batch.size=65536" \
    --producer-property "compression.type=snappy" \
    --producer-property "acks=all"

  log "Synthetic production complete (${NUM_EVENTS} events)"
}

# Perf: raw-throughput benchmark using kafka-producer-perf-test (does not exercise
# application semantics — use alongside `seed`/`synthetic` to stress the broker).
perf() {
  log "kafka-producer-perf-test: ${NUM_EVENTS} records, ${RECORD_SIZE}B, ${THROUGHPUT} msg/s"
  kafka-producer-perf-test.sh \
    --topic "${TOPIC}" \
    --num-records "${NUM_EVENTS}" \
    --record-size "${RECORD_SIZE}" \
    --throughput "${THROUGHPUT}" \
    --producer-props \
        bootstrap.servers="${BOOTSTRAP_SERVERS}" \
        acks=all \
        linger.ms=20 \
        batch.size=65536 \
        compression.type=snappy
}

# ---- dispatch ---------------------------------------------------------------
cmd="${1:-help}"
case "${cmd}" in
  create-topic) create_topic ;;
  seed)         seed ;;
  synthetic)    synthetic ;;
  perf)         perf ;;
  help|*)
    sed -n '1,40p' "$0"
    exit 0
    ;;
esac
