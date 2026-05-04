#!/usr/bin/env bash
# =============================================================================
# hex-scaffold — DocumentDB metal-tier k6 load-test orchestrator
#
# Usage:
#   loadtest-documentdb.sh deploy   <metal>   # helm upgrade for the tier
#   loadtest-documentdb.sh apply    <metal>   # create configmap + TestRun
#   loadtest-documentdb.sh wait               # block until run finishes
#   loadtest-documentdb.sh logs               # follow runner logs
#   loadtest-documentdb.sh summary            # print per-runner summaries
#   loadtest-documentdb.sh cleanup            # delete TestRun + ConfigMap
#   loadtest-documentdb.sh run      <metal>   # deploy → apply → wait → summary → cleanup
#
# metal ∈ {bronze, silver, gold, platinum, platinum-plus}.
#
# Required env (no defaults — set before run):
#   ADMIN_PWD       admin password for DocumentDB clusters (literal)
#
# Optional env:
#   NAMESPACE=hex-scaffold
#   PARALLELISM=6
#   TIMEOUT=30m
#   APP_INSIGHTS_CONNECTION_STRING (passed through to chart)
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

NAMESPACE="${NAMESPACE:-hex-scaffold}"
TESTRUN_NAME="hex-scaffold-documentdb-loadtest"
CONFIGMAP_NAME="hex-scaffold-loadtest"
PARALLELISM="${PARALLELISM:-6}"
TIMEOUT="${TIMEOUT:-30m}"
SCRIPT_FILE="${SCRIPT_DIR}/rest-api-loadtest-documentdb.js"
TESTRUN_TEMPLATE="${SCRIPT_DIR}/testrun-documentdb.yaml"
HELM_CHART="${REPO_ROOT}/deploy/helm/hex-scaffold"

CHART_RELEASE="hex-scaffold"
ADMIN_USER="dbuser"

log()   { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
fatal() { log "ERROR: $*" >&2; exit 1; }

mongo_conn_string_for() {
  local metal="$1"
  if [[ -z "${ADMIN_PWD:-}" ]]; then
    fatal "ADMIN_PWD env var is required (admin password for DocumentDB clusters)"
  fi
  # URL-encode literal '$' to %24 for MongoDB connection string parsing.
  local encoded_pwd
  encoded_pwd="$(python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1], safe=""))' "$ADMIN_PWD")"
  printf 'mongodb+srv://%s:%s@documentdb-%s.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000' \
    "$ADMIN_USER" "$encoded_pwd" "$metal"
}

deploy() {
  local metal="${1:?usage: deploy <metal>}"
  case "$metal" in bronze|silver|gold|platinum|platinum-plus) ;; *) fatal "unknown metal: $metal" ;; esac

  local values_file="${SCRIPT_DIR}/values-documentdb-${metal}.yaml"
  [[ -r "$values_file" ]] || fatal "values overlay not found: $values_file"

  local conn_string
  conn_string="$(mongo_conn_string_for "$metal")"

  log "Helm upgrade $CHART_RELEASE → tier=$metal"
  helm upgrade --install "$CHART_RELEASE" "$HELM_CHART" \
    -n "$NAMESPACE" \
    -f "$values_file" \
    --set "secrets.mongoConnectionString=${conn_string}" \
    ${APP_INSIGHTS_CONNECTION_STRING:+--set "secrets.appInsightsConnectionString=${APP_INSIGHTS_CONNECTION_STRING}"} \
    --wait --timeout 5m

  log "Waiting for app rollout"
  kubectl rollout status deployment/hex-scaffold -n "$NAMESPACE" --timeout=5m
  log "Deploy complete"
}

apply_test() {
  local metal="${1:?usage: apply <metal>}"

  log "Creating ConfigMap $CONFIGMAP_NAME from $SCRIPT_FILE"
  kubectl create configmap "$CONFIGMAP_NAME" \
    --from-file="rest-api-loadtest-documentdb.js=$SCRIPT_FILE" \
    -n "$NAMESPACE" \
    --dry-run=client -o yaml | kubectl apply -f -

  log "Rendering TestRun (TIER=$metal NAMESPACE=$NAMESPACE)"
  local rendered
  rendered="$(sed -e "s/__NAMESPACE__/$NAMESPACE/g" -e "s/__TIER__/$metal/g" "$TESTRUN_TEMPLATE")"

  log "Applying TestRun $TESTRUN_NAME"
  printf '%s' "$rendered" | kubectl apply -n "$NAMESPACE" -f -
}

wait_run() {
  log "Waiting for TestRun $TESTRUN_NAME to finish (timeout $TIMEOUT)"
  local deadline=$(($(date +%s) + $(parse_timeout "$TIMEOUT")))
  while [[ $(date +%s) -lt $deadline ]]; do
    local stage
    stage="$(kubectl get testrun "$TESTRUN_NAME" -n "$NAMESPACE" -o jsonpath='{.status.stage}' 2>/dev/null || echo "")"
    case "$stage" in
      finished|stopped|error)
        log "TestRun stage=$stage"
        return 0
        ;;
      "")
        log "  (no status yet)"
        ;;
      *)
        log "  stage=$stage"
        ;;
    esac
    sleep 30
  done
  fatal "TestRun did not finish within $TIMEOUT"
}

parse_timeout() {
  local t="$1"
  case "$t" in
    *m) echo $(( ${t%m} * 60 )) ;;
    *s) echo "${t%s}" ;;
    *)  echo "$t" ;;
  esac
}

logs_follow() {
  kubectl logs -n "$NAMESPACE" -l "k6_cr=$TESTRUN_NAME" -f --max-log-requests 10 --tail=100
}

summary() {
  log "Per-runner k6 summary"
  for pod in $(kubectl get pods -n "$NAMESPACE" -l "k6_cr=$TESTRUN_NAME" -o name 2>/dev/null); do
    echo
    echo "=== $pod ==="
    kubectl logs -n "$NAMESPACE" "$pod" --tail=80 2>&1 | tail -60
  done
}

cleanup() {
  log "Deleting TestRun $TESTRUN_NAME (idempotent)"
  kubectl delete testrun "$TESTRUN_NAME" -n "$NAMESPACE" --ignore-not-found
  log "Deleting ConfigMap $CONFIGMAP_NAME (idempotent)"
  kubectl delete configmap "$CONFIGMAP_NAME" -n "$NAMESPACE" --ignore-not-found
}

run() {
  local metal="${1:?usage: run <metal>}"
  deploy "$metal"
  apply_test "$metal"
  wait_run
  summary
  cleanup
}

main() {
  local cmd="${1:-}"; shift || true
  case "$cmd" in
    deploy)   deploy "$@" ;;
    apply)    apply_test "$@" ;;
    wait)     wait_run ;;
    logs)     logs_follow ;;
    summary)  summary ;;
    cleanup)  cleanup ;;
    run)      run "$@" ;;
    ""|help|-h|--help)
      sed -n '2,/^# =====/p' "${BASH_SOURCE[0]}"
      ;;
    *)
      fatal "unknown subcommand: $cmd (try help)"
      ;;
  esac
}
main "$@"
