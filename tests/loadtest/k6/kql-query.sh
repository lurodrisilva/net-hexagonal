#!/usr/bin/env bash
# =============================================================================
# kql-query.sh — Wrapper around `az monitor log-analytics query` for the
#                Application Insights workspace that backs hex-scaffold's
#                `app-ins-test` resource.
#
# Defaults:
#   WORKSPACE: 7104c6dc-8269-4283-9699-1840c52bbbe0  (DefaultWorkspace-...-CQ)
#   TIMESPAN:  PT1H                                  (last 1 hour)
#   FORMAT:    table                                 (use FORMAT=json for raw)
#
# Hits the Log Analytics data plane endpoint
#   https://api.loganalytics.io/v1/workspaces/{id}/query
# (v1-style response: {tables:[{name, columns, rows}]})
# Auth: AAD token from `az login`, audience https://api.loganalytics.io
#
# Companion piece to loadtest-pgsql-pp.sh — same shell style
# (set -euo pipefail, env-var defaults, snake_case, ISO-8601 logs).
#
# Usage
#   ./kql-query.sh '<KQL>'                        # query, default 1h window
#   ./kql-query.sh -t PT15M '<KQL>'              # 15-minute window
#   ./kql-query.sh -t 'PT5M' -f json '<KQL>'     # JSON output
#   ./kql-query.sh -p '<file.kql>'               # read query from file
#   ./kql-query.sh recent-errors                 # named recipe (see RECIPES below)
#   ./kql-query.sh recent-errors -t PT15M
#   ./kql-query.sh top-time-deps
#   ./kql-query.sh slow-requests
#   ./kql-query.sh test-window 2026-05-02T19:01:33Z 2026-05-02T19:05:06Z
#
# Environment (with defaults)
#   WORKSPACE=7104c6dc-8269-4283-9699-1840c52bbbe0
#   ROLE=hex-scaffold                  # AppRoleName filter for canned queries
#   TIMESPAN=PT1H                      # ISO-8601 duration
#   FORMAT=table                       # table | json | jsonc | tsv | yaml
#   APP_NAME=loadtest-kql/1.0          # request_app_name property
# =============================================================================
set -euo pipefail

WORKSPACE="${WORKSPACE:-7104c6dc-8269-4283-9699-1840c52bbbe0}"
ROLE="${ROLE:-hex-scaffold}"
TIMESPAN="${TIMESPAN:-PT1H}"
FORMAT="${FORMAT:-table}"
APP_NAME="${APP_NAME:-loadtest-kql/1.0}"

log()   { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
fatal() { log "ERROR: $*" >&2; exit 1; }

require() {
  command -v az >/dev/null 2>&1 || fatal "az CLI not found on PATH"
}

ensure_extension() {
  if ! az extension list --query "[?name=='log-analytics'] | [0].name" -o tsv 2>/dev/null | grep -q .; then
    log "Installing 'log-analytics' az extension (one-time)"
    az extension add --name log-analytics --only-show-errors >/dev/null 2>&1 \
      || fatal "Failed to install az extension 'log-analytics'"
  fi
}

run_query() {
  local query="$1"
  log "workspace=${WORKSPACE} timespan=${TIMESPAN} format=${FORMAT}"
  log "query: $(printf '%s' "$query" | tr '\n' ' ' | head -c 200)..."
  az monitor log-analytics query \
    --workspace "$WORKSPACE" \
    --analytics-query "$query" \
    --timespan "$TIMESPAN" \
    -o "$FORMAT"
}

# -----------------------------------------------------------------------------
# Recipes — named diagnostic queries calibrated for the load-test workflow
# -----------------------------------------------------------------------------
recipe_recent_errors() {
  cat <<KQL
AppRequests
| where AppRoleName == "${ROLE}"
| where Success == false
| summarize
    failures = sum(ItemCount),
    sample_url = any(Url),
    distinct_clients = dcount(ClientIP)
  by ResultCode, bin(TimeGenerated, 1m)
| order by TimeGenerated desc, failures desc
KQL
}

recipe_top_time_deps() {
  cat <<KQL
AppDependencies
| where AppRoleName == "${ROLE}"
| summarize
    total_ms = sum(DurationMs),
    p95      = percentile(DurationMs, 95),
    calls    = sum(ItemCount),
    failures = sumif(ItemCount, Success == false)
  by Type, Target, Name
| top 20 by total_ms
KQL
}

recipe_slow_requests() {
  cat <<KQL
AppRequests
| where AppRoleName == "${ROLE}"
| summarize
    p50 = percentile(DurationMs, 50),
    p95 = percentile(DurationMs, 95),
    p99 = percentile(DurationMs, 99),
    requests = sum(ItemCount)
  by Name, ResultCode, bin(TimeGenerated, 1m)
| where p95 > 200
| order by TimeGenerated desc, p95 desc
KQL
}

recipe_test_window() {
  local from="$1" to="$2"
  cat <<KQL
let from = datetime(${from});
let to   = datetime(${to});
AppRequests
| where AppRoleName == "${ROLE}"
| where TimeGenerated between (from .. to)
| summarize
    requests = sum(ItemCount),
    failures = sumif(ItemCount, Success == false),
    p50 = percentile(DurationMs, 50),
    p95 = percentile(DurationMs, 95),
    p99 = percentile(DurationMs, 99)
  by Name, ResultCode, bin(TimeGenerated, 1m)
| order by TimeGenerated asc, Name asc
KQL
}

recipe_top_queries() {
  # Top-time queries from Application Insights AppDependencies (only works if
  # SQL/HTTP dependencies are tracked) — falls back to AppRequests if empty.
  cat <<KQL
let win = ${TIMESPAN};
AppDependencies
| where AppRoleName == "${ROLE}"
| where Type in ("SQL", "Postgres", "Http")
| summarize
    total_ms = sum(DurationMs),
    p95 = percentile(DurationMs, 95),
    calls = sum(ItemCount)
  by Type, Target, Name
| top 20 by total_ms
KQL
}

usage() {
  sed -n '2,33p' "$0" >&2
}

# -----------------------------------------------------------------------------
# Argparse
# -----------------------------------------------------------------------------
QUERY=""
QUERY_FILE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    -t|--timespan)  TIMESPAN="$2"; shift 2 ;;
    -f|--format)    FORMAT="$2"; shift 2 ;;
    -w|--workspace) WORKSPACE="$2"; shift 2 ;;
    -r|--role)      ROLE="$2"; shift 2 ;;
    -p|--from-file) QUERY_FILE="$2"; shift 2 ;;
    -h|--help)      usage; exit 0 ;;
    --)             shift; break ;;
    -*)             fatal "unknown option: $1" ;;
    *)              break ;;
  esac
done

require
ensure_extension

# Named recipe?
case "${1:-}" in
  recent-errors)
    QUERY="$(recipe_recent_errors)"
    ;;
  top-time-deps)
    QUERY="$(recipe_top_time_deps)"
    ;;
  slow-requests)
    QUERY="$(recipe_slow_requests)"
    ;;
  top-queries)
    QUERY="$(recipe_top_queries)"
    ;;
  test-window)
    [[ $# -eq 3 ]] || fatal "test-window needs FROM_ISO TO_ISO"
    QUERY="$(recipe_test_window "$2" "$3")"
    ;;
  "")
    if [[ -n "$QUERY_FILE" ]]; then
      [[ -f "$QUERY_FILE" ]] || fatal "file not found: $QUERY_FILE"
      QUERY="$(cat "$QUERY_FILE")"
    else
      usage
      exit 1
    fi
    ;;
  *)
    QUERY="$1"
    ;;
esac

run_query "$QUERY"
