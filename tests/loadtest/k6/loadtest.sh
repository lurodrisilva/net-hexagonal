#!/usr/bin/env bash
# =============================================================================
# hex-scaffold — k6 REST load test orchestrator (in-cluster k6-Operator)
#
# Wraps the kubectl invocations needed to drive the existing
# rest-api-loadtest.js + testrun.yaml pair against a hex-scaffold deployment.
# Subcommands compose a one-shot run and the individual phases for debugging.
#
# Companion piece to tests/loadtest/kafka/load-test.sh — same shell style
# (set -euo pipefail, env-var defaults, snake_case subcommands, ISO-8601
# timestamped logs).
#
# Prerequisites
#   * kubectl on PATH, current-context pointing at the target cluster
#   * k6-Operator installed in-cluster (https://github.com/grafana/k6-operator)
#   * hex-scaffold deployed in NAMESPACE with a ClusterIP Service named
#     'hex-scaffold' on port 80 (default Helm chart layout)
#
# Usage
#   loadtest.sh [SUBCOMMAND]
#
# Subcommands
#   prereq     Verify cluster prerequisites without changing anything
#   apply      Create/update the script ConfigMap and apply the TestRun
#   status     Print current TestRun phase + describe
#   logs       Tail logs from runner pods (Ctrl-C to detach)
#   wait       Block until TestRun stage=finished or stage=error
#   summary    Print the per-runner k6 summary (final iteration of `logs`)
#   cleanup    Delete TestRun + ConfigMap (idempotent)
#   run        apply -> stream logs -> wait -> status -> cleanup (one shot)
#
# Environment (with defaults)
#   NAMESPACE=hex-scaffold
#   BASE_URL=http://hex-scaffold.<NAMESPACE>.svc:80
#   TESTRUN_NAME=hex-scaffold-rest-loadtest
#   CONFIGMAP_NAME=hex-scaffold-loadtest
#   PARALLELISM=4
#   TIMEOUT=30m                 # `wait` deadline
#   SCRIPT=<dir>/rest-api-loadtest.js
#   TESTRUN_YAML=<dir>/testrun.yaml
#
# Examples
#   ./loadtest.sh run                     # full execution + auto cleanup
#   NAMESPACE=staging ./loadtest.sh apply # ship to a different namespace
#   ./loadtest.sh logs                    # follow runner output
#   PARALLELISM=8 ./loadtest.sh run       # double the runner replicas
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"

NAMESPACE="${NAMESPACE:-hex-scaffold}"
BASE_URL="${BASE_URL:-http://hex-scaffold.${NAMESPACE}.svc:80}"
TESTRUN_NAME="${TESTRUN_NAME:-hex-scaffold-rest-loadtest}"
CONFIGMAP_NAME="${CONFIGMAP_NAME:-hex-scaffold-loadtest}"
PARALLELISM="${PARALLELISM:-4}"
TIMEOUT="${TIMEOUT:-30m}"
SCRIPT="${SCRIPT:-${SCRIPT_DIR}/rest-api-loadtest.js}"
TESTRUN_YAML="${TESTRUN_YAML:-${SCRIPT_DIR}/testrun.yaml}"

log()   { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
fatal() { log "ERROR: $*" >&2; exit 1; }

require() {
  command -v "$1" >/dev/null 2>&1 || fatal "Missing required tool on PATH: $1"
}

# ---- prereq -----------------------------------------------------------------

prereq() {
  require kubectl
  [[ -r "${SCRIPT}" ]]       || fatal "k6 script not readable: ${SCRIPT}"
  [[ -r "${TESTRUN_YAML}" ]] || fatal "TestRun manifest not readable: ${TESTRUN_YAML}"
  kubectl get ns "${NAMESPACE}" >/dev/null 2>&1 \
    || fatal "Namespace '${NAMESPACE}' not found in current kubectl context"
  kubectl api-resources --api-group=k6.io 2>/dev/null | grep -qw TestRun \
    || fatal "k6.io/TestRun CRD not installed; install k6-Operator first"
  kubectl -n "${NAMESPACE}" get svc hex-scaffold >/dev/null 2>&1 \
    || log "WARN: Service 'hex-scaffold' not found in '${NAMESPACE}'; runners may fail to connect"
  log "Prerequisites OK (namespace=${NAMESPACE}, target=${BASE_URL})"
}

# ---- apply ------------------------------------------------------------------

# Render the TestRun manifest with the current env-var settings substituted in.
# We avoid an external templating dependency (yq, helm, envsubst-from-coreutils
# is available everywhere; sed handles the four fields the orchestrator owns).
render_testrun() {
  sed \
    -e "s|name: hex-scaffold-rest-loadtest$|name: ${TESTRUN_NAME}|" \
    -e "s|name: hex-scaffold-loadtest$|name: ${CONFIGMAP_NAME}|" \
    -e "s|parallelism: [0-9][0-9]*|parallelism: ${PARALLELISM}|" \
    -e "s|value: \"http://hex-scaffold.default.svc:80\"|value: \"${BASE_URL}\"|" \
    "${TESTRUN_YAML}"
}

apply() {
  prereq
  log "Creating ConfigMap '${CONFIGMAP_NAME}' in '${NAMESPACE}' from ${SCRIPT##*/}"
  kubectl -n "${NAMESPACE}" create configmap "${CONFIGMAP_NAME}" \
    --from-file=rest-api-loadtest.js="${SCRIPT}" \
    --dry-run=client -o yaml | kubectl apply -f -

  log "Applying TestRun '${TESTRUN_NAME}' (parallelism=${PARALLELISM}, BASE_URL=${BASE_URL})"
  render_testrun | kubectl -n "${NAMESPACE}" apply -f -
}

# ---- status / logs / wait ---------------------------------------------------

status() {
  prereq
  if ! kubectl -n "${NAMESPACE}" get testrun "${TESTRUN_NAME}" >/dev/null 2>&1; then
    log "TestRun '${TESTRUN_NAME}' does not exist in '${NAMESPACE}'"
    return 1
  fi
  kubectl -n "${NAMESPACE}" get testrun "${TESTRUN_NAME}" -o wide
  echo
  kubectl -n "${NAMESPACE}" describe testrun "${TESTRUN_NAME}" \
    | sed -n '/Status:/,/Events:/p'
}

logs() {
  prereq
  log "Tailing runner logs (label k6_cr=${TESTRUN_NAME}). Ctrl-C to detach."
  kubectl -n "${NAMESPACE}" logs -l "k6_cr=${TESTRUN_NAME}" \
    --all-containers=true --tail=-1 --max-log-requests=20 -f
}

# Convert TIMEOUT (e.g. "30m", "1h", "90s") into seconds without bc/awk math.
parse_timeout_seconds() {
  local v="$1" unit="${1//[0-9]/}" num="${1//[!0-9]/}"
  [[ -z "${num}" ]] && fatal "TIMEOUT='${v}' is not a positive integer with unit"
  case "${unit:-s}" in
    s|"") echo $((num)) ;;
    m)    echo $((num * 60)) ;;
    h)    echo $((num * 3600)) ;;
    *)    fatal "Unknown TIMEOUT unit '${unit}'; expected s|m|h" ;;
  esac
}

wait_for_finish() {
  prereq
  local deadline now stage
  deadline=$(( $(date +%s) + $(parse_timeout_seconds "${TIMEOUT}") ))
  log "Waiting for TestRun '${TESTRUN_NAME}' to reach stage=finished (timeout=${TIMEOUT})"
  while :; do
    stage="$(kubectl -n "${NAMESPACE}" get testrun "${TESTRUN_NAME}" \
      -o jsonpath='{.status.stage}' 2>/dev/null || true)"
    case "${stage}" in
      finished) log "TestRun finished cleanly."; return 0 ;;
      error)    log "TestRun reported stage=error."; return 1 ;;
    esac
    now=$(date +%s)
    if (( now > deadline )); then
      fatal "Timed out after ${TIMEOUT} (last stage='${stage:-<empty>}')"
    fi
    sleep 5
  done
}

# Print just the textSummary block emitted by handleSummary in the JS script.
# Runner pods write it to stdout at the very end, so a one-shot logs read
# (no -f) is sufficient.
summary() {
  prereq
  log "Fetching summary block from runner logs"
  kubectl -n "${NAMESPACE}" logs -l "k6_cr=${TESTRUN_NAME}" \
    --all-containers=true --tail=-1 --max-log-requests=20 \
    | awk '/======== hex-scaffold REST load-test summary ========/,/======================================================/'
}

# ---- cleanup / run ----------------------------------------------------------

cleanup() {
  log "Deleting TestRun '${TESTRUN_NAME}' (if present)"
  kubectl -n "${NAMESPACE}" delete testrun "${TESTRUN_NAME}" --ignore-not-found
  log "Deleting ConfigMap '${CONFIGMAP_NAME}' (if present)"
  kubectl -n "${NAMESPACE}" delete configmap "${CONFIGMAP_NAME}" --ignore-not-found
}

# One-shot: apply, stream logs in the background while we poll for finish,
# then cleanup. Cleanup runs on any exit path (success, failure, Ctrl-C) so
# the cluster doesn't leak a stuck TestRun + ConfigMap if the operator
# misbehaves or the user interrupts.
run() {
  apply
  log "Streaming runner logs in background while the test executes"
  logs &
  local logs_pid=$!
  cleanup_on_exit() {
    if [[ -n "${logs_pid:-}" ]] && kill -0 "${logs_pid}" 2>/dev/null; then
      kill "${logs_pid}" 2>/dev/null || true
      wait "${logs_pid}" 2>/dev/null || true
    fi
    cleanup
  }
  trap cleanup_on_exit EXIT INT TERM
  if wait_for_finish; then
    log "Final status:"
    status || true
    return 0
  else
    log "Test did not finish cleanly; final status follows for debugging:"
    status || true
    return 1
  fi
}

# ---- dispatch ---------------------------------------------------------------

case "${1:-help}" in
  prereq)  prereq ;;
  apply)   apply ;;
  status)  status ;;
  logs)    logs ;;
  wait)    wait_for_finish ;;
  summary) summary ;;
  cleanup) cleanup ;;
  run)     run ;;
  help|-h|--help|"")
    sed -n '2,50p' "${BASH_SOURCE[0]}" | sed 's|^# \{0,1\}||'
    ;;
  *)
    fatal "Unknown subcommand '$1'. Run '$0 help' for usage."
    ;;
esac
