#!/usr/bin/env bash
# Smoke-test the Hex.Scaffold API: one request per route + verb implemented.
#
# Routes covered:
#   GET    /healthz                       (liveness)
#   GET    /ready                         (readiness)
#   POST   /v2/core/accounts              (create)
#   GET    /v2/core/accounts              (list)
#   GET    /v2/core/accounts/{id}         (retrieve)
#   POST   /v2/core/accounts/{id}         (update — Stripe uses POST for partial-update)
#
# Usage:
#   ./request.sh                          # hits http://localhost:8080
#   BASE_URL=https://api.example.com ./request.sh
#
# Exits non-zero if any request returns a status outside its expected range.

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"

# ---- helpers ---------------------------------------------------------------

red()    { printf '\033[31m%s\033[0m\n' "$*"; }
green()  { printf '\033[32m%s\033[0m\n' "$*"; }
yellow() { printf '\033[33m%s\033[0m\n' "$*"; }
bold()   { printf '\033[1m%s\033[0m\n' "$*"; }

# request <label> <expected-status-regex> <curl args...>
# Prints status + body, fails fast if status doesn't match the regex.
request() {
  local label="$1"; shift
  local expect="$1"; shift

  bold ""
  bold "──▶ ${label}"

  local body_file
  body_file="$(mktemp)"
  local status
  status="$(curl -sS -o "${body_file}" -w '%{http_code}' "$@")"

  if [[ "${status}" =~ ^${expect}$ ]]; then
    green "  status: ${status}"
  else
    red   "  status: ${status} (expected ${expect})"
  fi

  if [[ -s "${body_file}" ]]; then
    if command -v jq >/dev/null 2>&1; then
      jq . < "${body_file}" 2>/dev/null || cat "${body_file}"
    else
      cat "${body_file}"
    fi
    echo
  fi

  if [[ ! "${status}" =~ ^${expect}$ ]]; then
    rm -f "${body_file}"
    exit 1
  fi
  rm -f "${body_file}"
}

# json_field <file> <key>: extract a top-level JSON string field. Uses jq if
# present, falls back to a tolerant grep/sed for environments without jq.
json_field() {
  local file="$1" key="$2"
  if command -v jq >/dev/null 2>&1; then
    jq -r ".${key} // empty" < "${file}"
  else
    sed -n "s/.*\"${key}\"[[:space:]]*:[[:space:]]*\"\\([^\"]*\\)\".*/\\1/p" < "${file}" | head -n1
  fi
}

# ---- 1. Health -------------------------------------------------------------

request "GET /healthz" "200" \
  -X GET "${BASE_URL}/healthz"

# ---- 2. Readiness ----------------------------------------------------------

request "GET /ready" "200" \
  -X GET "${BASE_URL}/ready"

# ---- 3. Create account -----------------------------------------------------
# Capture the response so we can use the generated acct_… id in the next calls.

CREATE_BODY="$(mktemp)"
trap 'rm -f "${CREATE_BODY}"' EXIT

bold ""
bold "──▶ POST /v2/core/accounts"
CREATE_STATUS="$(curl -sS -o "${CREATE_BODY}" -w '%{http_code}' \
  -X POST "${BASE_URL}/v2/core/accounts" \
  -H 'Content-Type: application/json' \
  -d '{
    "display_name": "Acme Co",
    "contact_email": "ops@acme.test",
    "applied_configurations": ["customer"]
  }')"

if [[ "${CREATE_STATUS}" == "200" ]]; then
  green "  status: ${CREATE_STATUS}"
else
  red   "  status: ${CREATE_STATUS} (expected 200)"
  cat "${CREATE_BODY}"; echo
  exit 1
fi

if command -v jq >/dev/null 2>&1; then
  jq . < "${CREATE_BODY}"
else
  cat "${CREATE_BODY}"; echo
fi

ACCOUNT_ID="$(json_field "${CREATE_BODY}" id)"
if [[ -z "${ACCOUNT_ID}" ]]; then
  red "  could not parse account id from create response"
  exit 1
fi
yellow "  captured id: ${ACCOUNT_ID}"

# ---- 4. List accounts ------------------------------------------------------

request "GET /v2/core/accounts" "200" \
  -X GET "${BASE_URL}/v2/core/accounts?limit=5"

# ---- 5. Retrieve account ---------------------------------------------------

request "GET /v2/core/accounts/{id}" "200" \
  -X GET "${BASE_URL}/v2/core/accounts/${ACCOUNT_ID}"

# ---- 6. Update account -----------------------------------------------------
# Stripe-style partial update: absent keys are left alone, explicit nulls clear.

request "POST /v2/core/accounts/{id}" "200" \
  -X POST "${BASE_URL}/v2/core/accounts/${ACCOUNT_ID}" \
  -H 'Content-Type: application/json' \
  -d '{"display_name":"Acme Co. (updated)"}'

bold ""
green "All routes responded as expected."
