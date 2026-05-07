#!/usr/bin/env bash
# =====================================================================
# Phase 5 driver — Kafka inbound loadtest, one run at a time.
#
# Usage:
#   loadtest-kafka.sh <tier> <repo>
#     tier = silver | gold | platinum
#     repo = postgres | mongo
#
# Environment overrides (sane defaults from env or hardcoded fallbacks):
#   AI_CONN            App Insights connection string
#   PG_PASSWORD        PostgreSQL admin password (URL-encoded if special chars)
#   DOCDB_PASSWORD     DocumentDB admin password (URL-encoded if special chars)
#   AI_RESOURCE_ID     /subscriptions/.../resourceGroups/<rg>/providers/microsoft.insights/components/<name>
#   PG_RG              resources-test-rg
#   DOCDB_RG           resources-test-rg
#   RUN_CAP_SECONDS    900   (15 min hard cap)
#   POLL_INTERVAL_SEC  30
#
# Captures all evidence under .omc/research/kafka-loadtest/<runid>/
# =====================================================================

set -uo pipefail

TIER="${1:-silver}"
REPO="${2:-postgres}"

case "$TIER" in silver|gold|platinum) ;; *) echo "bad tier"; exit 1 ;; esac
case "$REPO" in postgres|mongo) ;; *) echo "bad repo"; exit 1 ;; esac

SHORT_REPO=$([ "$REPO" = "postgres" ] && echo "pg" || echo "mongo")
PG_INSTANCE=$([ "$TIER" = "silver" ] && echo "pgsql-pp-silver-1" \
              || ([ "$TIER" = "gold" ] && echo "pgsql-pp-gold" \
                  || echo "pgsql-pp-platinum-1"))
DOCDB_INSTANCE="documentdb-${TIER}"
TOPIC="v2.core.accounts.loadtest.${TIER}.${SHORT_REPO}"
RUNID="$(date -u +%s)"
GROUP="hex-scaffold-loadtest-${TIER}-${REPO}-${RUNID}"
RUN_START_TS="$(date -u +%FT%TZ)"

ART_DIR=".omc/research/kafka-loadtest/${TIER}-${SHORT_REPO}-${RUNID}"
mkdir -p "$ART_DIR"

PG_RG="${PG_RG:-resources-test-rg}"
DOCDB_RG="${DOCDB_RG:-resources-test-rg}"
PG_PASSWORD="${PG_PASSWORD:-\$kFFb23j%KKll}"
DOCDB_PASSWORD="${DOCDB_PASSWORD:-\$kFFb23j%KKll}"
AI_CONN="${AI_CONN:-InstrumentationKey=84057efc-9957-45a5-b19b-5c60cd12890e;IngestionEndpoint=https://brazilsouth-1.in.applicationinsights.azure.com/;LiveEndpoint=https://brazilsouth.livediagnostics.monitor.azure.com/;ApplicationId=e65c9fd9-b510-48e7-894f-32f69a230d6d}"
AI_APP_ID="${AI_APP_ID:-e65c9fd9-b510-48e7-894f-32f69a230d6d}"
RUN_CAP_SECONDS="${RUN_CAP_SECONDS:-900}"
POLL_INTERVAL_SEC="${POLL_INTERVAL_SEC:-30}"
P95_BREACH_THRESHOLD_MS="${P95_BREACH_THRESHOLD_MS:-200}"

# URL-encode the password for connection strings ($ -> %24, % -> %25).
urlenc() { python3 -c 'import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1], safe=""))' "$1"; }
PG_PASS_ENC="$(urlenc "$PG_PASSWORD")"
DOCDB_PASS_ENC="$(urlenc "$DOCDB_PASSWORD")"

if [ "$REPO" = "postgres" ]; then
  REPO_CONN="Host=${PG_INSTANCE}.postgres.database.azure.com;Database=postgres;Username=adminpg;Password=${PG_PASSWORD};Port=5432;Maximum Pool Size=100;SslMode=Require;Trust Server Certificate=true"
  AZ_RES_ID="/subscriptions/df21ed78-be77-40e3-9184-38eb23175791/resourceGroups/${PG_RG}/providers/Microsoft.DBforPostgreSQL/flexibleServers/${PG_INSTANCE}"
else
  REPO_CONN="mongodb+srv://dbuser:${DOCDB_PASS_ENC}@${DOCDB_INSTANCE}.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000"
  AZ_RES_ID="/subscriptions/df21ed78-be77-40e3-9184-38eb23175791/resourceGroups/${DOCDB_RG}/providers/Microsoft.DocumentDB/mongoClusters/${DOCDB_INSTANCE}"
fi

cat > "${ART_DIR}/meta.json" <<EOF
{
  "tier": "${TIER}",
  "repo": "${REPO}",
  "runId": "${RUNID}",
  "topic": "${TOPIC}",
  "consumerGroup": "${GROUP}",
  "runStartTs": "${RUN_START_TS}",
  "azureResourceId": "${AZ_RES_ID}",
  "pgInstance": "${PG_INSTANCE}",
  "docdbInstance": "${DOCDB_INSTANCE}",
  "runCapSeconds": ${RUN_CAP_SECONDS}
}
EOF

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*" | tee -a "${ART_DIR}/run.log"; }

log "=== Run start === tier=${TIER} repo=${REPO} runid=${RUNID}"
log "topic=${TOPIC} group=${GROUP}"
log "artifacts=${ART_DIR}"

# ---------------------------------------------------------------------
# Pre-flight — helm upgrade hex-scaffold to per-tier overlay.
# ---------------------------------------------------------------------
log "helm upgrade hex-scaffold (tier=${TIER}, repo=${REPO})..."
helm upgrade --install hex-scaffold deploy/helm/hex-scaffold \
  -n hex-scaffold \
  -f deploy/helm/hex-scaffold/values.yaml \
  -f deploy/helm/hex-scaffold/values-aks-test-loadtest.yaml \
  -f "deploy/helm/hex-scaffold/values-loadtest-kafka-${TIER}.yaml" \
  --set "features.persistence=${REPO}" \
  --set "kafka.inboundTopic=${TOPIC}" \
  --set "secrets.kafkaBootstrapServers=hex-scaffold-loadtest-kafka-bootstrap.messaging-system.svc:9092" \
  --set "secrets.kafkaConsumerGroupId=${GROUP}" \
  --set-string "secrets.appInsightsConnectionString=${AI_CONN}" \
  --set-string "secrets.${REPO}ConnectionString=${REPO_CONN}" \
  >>"${ART_DIR}/helm.log" 2>&1
HELM_RC=$?
if [ $HELM_RC -ne 0 ]; then log "helm upgrade FAILED rc=${HELM_RC}, see ${ART_DIR}/helm.log"; exit 2; fi
log "helm upgrade ok"

EXPECTED_REPLICAS=$([ "$TIER" = "silver" ] && echo 2 || ([ "$TIER" = "gold" ] && echo 4 || echo 8))
log "wait deploy ready (expect ${EXPECTED_REPLICAS} replicas)..."
kubectl rollout status -n hex-scaffold deploy/hex-scaffold --timeout=5m >>"${ART_DIR}/rollout.log" 2>&1
log "deploy ready"

# ---------------------------------------------------------------------
# Apply TestRun (substitute __TIER__ + __RUNID__).
# ---------------------------------------------------------------------
TR_FILE="tests/loadtest/k6/testrun-kafka-${SHORT_REPO}.yaml"
TR_RENDERED="${ART_DIR}/testrun.yaml"
sed -e "s|__TIER__|${TIER}|g" -e "s|__RUNID__|${RUNID}|g" "$TR_FILE" > "$TR_RENDERED"
log "apply ${TR_FILE} (rendered ${TR_RENDERED})"
kubectl apply -f "$TR_RENDERED" >>"${ART_DIR}/run.log" 2>&1

# ---------------------------------------------------------------------
# Watch loop — poll every POLL_INTERVAL_SEC. Stop on:
#   - TestRun finished / failed
#   - inbound_event_processing_duration_ms p95 >= ${P95_BREACH_THRESHOLD_MS} for >=2 samples
#   - Repo CPU > 80% sustained
#   - cap reached (RUN_CAP_SECONDS)
# Capture per-poll evidence to ART_DIR.
# ---------------------------------------------------------------------
RUN_BEGIN=$(date +%s)
P95_BREACH=0
CPU_BREACH=0
STOP_REASON=""

# Network-resilience helper. Wraps a command in `timeout` (Linux) or
# `gtimeout` (macOS via coreutils) so a hung DNS lookup or stuck az/kubectl
# call cannot extend a poll iteration past the budget. Returns the command's
# stdout on success; empty string on timeout or non-zero exit (caller treats
# both as "metric unavailable this iteration", same as before).
TIMEOUT_BIN="$(command -v gtimeout || command -v timeout || echo '')"
run_with_timeout() {
  local secs="$1"; shift
  if [ -n "$TIMEOUT_BIN" ]; then
    "$TIMEOUT_BIN" "$secs" "$@" 2>/dev/null || true
  else
    "$@" 2>/dev/null || true
  fi
}

while :; do
  NOW=$(date +%s)
  ELAPSED=$((NOW - RUN_BEGIN))
  if [ $ELAPSED -ge $RUN_CAP_SECONDS ]; then STOP_REASON="cap-reached"; break; fi

  # TestRun status — kubectl --request-timeout caps the wait at 15s.
  TR_STATE=$(kubectl --request-timeout=15s get testrun -n testing-system "hex-scaffold-kafka-loadtest-${SHORT_REPO}" -o jsonpath='{.status.stage}' 2>/dev/null || echo "absent")
  echo "$(date -u +%FT%TZ) elapsed=${ELAPSED}s testrun=${TR_STATE}" >>"${ART_DIR}/poll.log"
  if [ "$TR_STATE" = "finished" ] || [ "$TR_STATE" = "error" ]; then STOP_REASON="testrun-${TR_STATE}"; break; fi

  # PG / DocDB CPU via Azure Monitor — wrapped in timeout so DNS hangs cannot
  # stretch a poll iteration to multi-minute durations (silver-pg v2 lost
  # observability for ~70 min after a transient local DNS outage).
  if [ "$REPO" = "postgres" ]; then
    METRIC="cpu_percent"
  else
    METRIC="CpuPercent"
  fi
  CPU_AVG=$(run_with_timeout 20 az monitor metrics list --resource "$AZ_RES_ID" --metric "$METRIC" --interval PT1M --aggregation Average --output tsv --query 'value[0].timeseries[0].data[-1].average' | head -1)
  echo "$(date -u +%FT%TZ) cpu_avg=${CPU_AVG}" >>"${ART_DIR}/repo-cpu.log"
  if [ -n "$CPU_AVG" ] && python3 -c "import sys; sys.exit(0 if float('${CPU_AVG}') > 80 else 1)" 2>/dev/null; then
    CPU_BREACH=$((CPU_BREACH + 1))
    if [ $CPU_BREACH -ge 2 ]; then STOP_REASON="repo-cpu-breach"; break; fi
  else
    CPU_BREACH=0
  fi

  # AI p95 inbound_event_processing_duration_ms via az rest (timeout-wrapped).
  #
  # IMPORTANT: filter on customDimensions.{tier,repo}, NOT runId. The metric's
  # tag set (`event_type`, `tier`, `repo`) is bounded by the OTel View at
  # ObservabilityConfig.cs:144-149 to keep cardinality finite — runId is set as
  # an Activity tag (consumer.cs:98) and lands in `requests` / `dependencies`
  # customDimensions, never in `customMetrics`. v1/v2/v3 of this driver all
  # filtered on customDimensions.runId here and got count=0 every iteration.
  AI_QUERY="customMetrics | where name == 'inbound_event_processing_duration_ms' and customDimensions.tier == '${TIER}' and customDimensions.repo == '${REPO}' | where timestamp > ago(2m) | summarize p95=percentile(value, 95)"
  P95=$(run_with_timeout 25 az monitor app-insights query --app "$AI_APP_ID" --analytics-query "$AI_QUERY" --output tsv --query 'tables[0].rows[0][0]' | head -1)
  echo "$(date -u +%FT%TZ) p95_ms=${P95}" >>"${ART_DIR}/ai-p95.log"
  if [ -n "$P95" ] && python3 -c "import sys; sys.exit(0 if float('${P95}') >= ${P95_BREACH_THRESHOLD_MS} else 1)" 2>/dev/null; then
    P95_BREACH=$((P95_BREACH + 1))
    if [ $P95_BREACH -ge 2 ]; then STOP_REASON="p95-breach"; break; fi
  else
    P95_BREACH=0
  fi

  sleep $POLL_INTERVAL_SEC
done

log "stop reason: ${STOP_REASON}"

# ---------------------------------------------------------------------
# Drain k6 — CAPTURE BEFORE DELETE.
#
# Phase 5 v1 had a race: `kubectl delete` ran first, then `kubectl logs`
# returned "No resources found" because the runner pod was already gone.
# Phase 5 v2 added `kubectl cp /tmp/summary.json`, but it fails on
# Completed pods (`cannot exec into a container in a completed pod`).
# Phase 5 v3 (current): rely solely on `kubectl logs`, which DOES work on
# Completed pods, and parse the embedded k6 summary block from stdout.
# All metrics emitted by k6 (kafka_writer_*, iteration_duration, vus, etc.)
# print to stdout at end-of-run, so the log capture is sufficient.
# ---------------------------------------------------------------------
RUNNER_POD=$(kubectl --request-timeout=30s get pods -n testing-system \
  -l "k6_cr=hex-scaffold-kafka-loadtest-${SHORT_REPO}" \
  --no-headers -o custom-columns=:.metadata.name 2>/dev/null | head -1)
if [ -n "$RUNNER_POD" ]; then
  log "capture k6 evidence from runner pod ${RUNNER_POD}"
  kubectl --request-timeout=60s logs -n testing-system "$RUNNER_POD" \
    --all-containers --tail=10000 \
    >"${ART_DIR}/k6.log" 2>&1 || true
else
  log "WARN: no runner pod found for k6_cr=hex-scaffold-kafka-loadtest-${SHORT_REPO}"
fi
kubectl --request-timeout=30s delete -f "$TR_RENDERED" --ignore-not-found >>"${ART_DIR}/run.log" 2>&1

# Per-partition consumer-group lag snapshot
kubectl exec -n messaging-system hex-scaffold-loadtest-hex-scaffold-loadtest-pool-0 -c kafka -- \
  bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --describe --group "$GROUP" \
  >"${ART_DIR}/consumer-group-lag.log" 2>&1 || true

# ---------------------------------------------------------------------
# AI metrics snapshot + diagnostics.
#
# Phase 5 v1: every ai-final.json showed count=0 — query matched no rows.
# Three possibilities: (a) app didn't ship the metric at all, (b) shipped
# under a different name/table, (c) runId customDimension didn't propagate.
# Capture three queries so we can tell which is which:
# ---------------------------------------------------------------------

# Final p95 — filter on tier+repo (NOT runId, which is not a metric tag —
# see in-loop AI_QUERY comment). Window covers run + 60 min trail because:
#   1. Consumer keeps processing lag for many minutes after k6 producer cuts off.
#   2. App Insights ingestion lag is 5-15 min — records emitted at end-of-run
#      typically don't appear in customMetrics until well after this driver
#      has finished. The query is fired BOTH here (best-effort, may be early)
#      and AGAIN in the optional post-run sweep (`ai-final-late.json` below).
AI_FINAL_QUERY="customMetrics | where name == 'inbound_event_processing_duration_ms' | where timestamp between (datetime('${RUN_START_TS}') .. datetime('${RUN_START_TS}')+15m+60m) | where customDimensions.tier == '${TIER}' and customDimensions.repo == '${REPO}' | summarize p50=percentile(value,50), p95=percentile(value,95), p99=percentile(value,99), avg=avg(value), max=max(value), count=count()"
run_with_timeout 30 az monitor app-insights query --app "$AI_APP_ID" --analytics-query "$AI_FINAL_QUERY" --output json >"${ART_DIR}/ai-final.json" 2>&1 || true

# Diag 1 — same metric, all tier/repo combos in last 30 min. Surfaces
# leftover records from prior runs and confirms the metric pipeline at all.
AI_NORID_QUERY="customMetrics | where name == 'inbound_event_processing_duration_ms' and timestamp > ago(30m) | summarize count=count(), p95=percentile(value,95) by tostring(customDimensions.tier), tostring(customDimensions.repo)"
run_with_timeout 30 az monitor app-insights query --app "$AI_APP_ID" --analytics-query "$AI_NORID_QUERY" --output json >"${ART_DIR}/ai-diag-norid.json" 2>&1 || true

# Diag 2 — top customMetric names in the run window. If our metric isn't
# in the top list, the app isn't emitting it (US-004 ObservabilityConfig
# View / OTel meter registration broken).
AI_NAMES_QUERY="customMetrics | where timestamp > ago(30m) | summarize count=count() by name | top 30 by count"
run_with_timeout 30 az monitor app-insights query --app "$AI_APP_ID" --analytics-query "$AI_NAMES_QUERY" --output json >"${ART_DIR}/ai-diag-names.json" 2>&1 || true

# Diag 3 — any traffic at all in the run window across customMetrics, requests,
# traces, dependencies. Distinguishes "metric missing" from "AI dropping us".
# Filter by app role / kafka-related operation name since runId is not a metric tag.
AI_TRAFFIC_QUERY="union customMetrics, requests, traces, dependencies | where timestamp > ago(30m) | where (cloud_RoleName == 'hex-scaffold' or operation_Name contains 'kafka' or customDimensions.tier == '${TIER}') | summarize count=count() by itemType, cloud_RoleName"
run_with_timeout 30 az monitor app-insights query --app "$AI_APP_ID" --analytics-query "$AI_TRAFFIC_QUERY" --output json >"${ART_DIR}/ai-diag-traffic.json" 2>&1 || true

# Repo full-window CPU + IOPS
if [ "$REPO" = "postgres" ]; then
  az monitor metrics list --resource "$AZ_RES_ID" --metric cpu_percent,memory_percent,iops --interval PT1M --aggregation Average --output json >"${ART_DIR}/repo-metrics.json" 2>&1 || true
else
  az monitor metrics list --resource "$AZ_RES_ID" --metric CpuPercent,MemoryPercent,RequestUnitsConsumed --interval PT1M --aggregation Average --output json >"${ART_DIR}/repo-metrics.json" 2>&1 || true
fi

# ---------------------------------------------------------------------
# Cleanup — synthetic rows + consumer group.
# ---------------------------------------------------------------------
log "cleanup: synthetic rows via timestamp predicate (>= ${RUN_START_TS})"
if [ "$REPO" = "postgres" ]; then
  PG_HOST="${PG_INSTANCE}.postgres.database.azure.com"
  PGPASSWORD="$PG_PASSWORD" psql -h "$PG_HOST" -U adminpg -d postgres -At \
    -c "DELETE FROM accounts WHERE created >= '${RUN_START_TS}'::timestamptz;" \
    >"${ART_DIR}/cleanup.log" 2>&1 || true
else
  MONGO_URI="mongodb+srv://dbuser:${DOCDB_PASS_ENC}@${DOCDB_INSTANCE}.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false"
  mongosh "$MONGO_URI" --quiet --eval "
    db = db.getSiblingDB('hex-scaffold');
    print(JSON.stringify(db.accounts.deleteMany({ created: { \$gte: ISODate('${RUN_START_TS}') } })));
  " >"${ART_DIR}/cleanup.log" 2>&1 || true
fi

# Drop the per-run consumer group (idempotent — may not exist if k6 produced only).
kubectl exec -n messaging-system hex-scaffold-loadtest-hex-scaffold-loadtest-pool-0 -c kafka -- \
  bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --delete --group "$GROUP" \
  >>"${ART_DIR}/cleanup.log" 2>&1 || true

# ---------------------------------------------------------------------
# Final per-run summary.json
# ---------------------------------------------------------------------
ELAPSED_FINAL=$(($(date +%s) - RUN_BEGIN))
cat > "${ART_DIR}/summary.json" <<EOF
{
  "tier": "${TIER}",
  "repo": "${REPO}",
  "runId": "${RUNID}",
  "topic": "${TOPIC}",
  "stopReason": "${STOP_REASON}",
  "elapsedSeconds": ${ELAPSED_FINAL},
  "runStartTs": "${RUN_START_TS}",
  "azureResourceId": "${AZ_RES_ID}"
}
EOF

log "=== Run end === tier=${TIER} repo=${REPO} stopReason=${STOP_REASON} elapsed=${ELAPSED_FINAL}s"
log "artifacts=${ART_DIR}"
