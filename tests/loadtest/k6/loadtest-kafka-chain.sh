#!/usr/bin/env bash
# Chain runner — invokes loadtest-kafka.sh for the remaining ladder rungs.
# Caller pre-set: PATH (libpq), PG_PASSWORD, DOCDB_PASSWORD env vars.
#
# Usage:
#   ./loadtest-kafka-chain.sh <tier1>:<repo1> <tier2>:<repo2> ...
#   e.g. ./loadtest-kafka-chain.sh gold:postgres gold:mongo platinum:postgres platinum:mongo

set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DRIVER="${SCRIPT_DIR}/loadtest-kafka.sh"

for spec in "$@"; do
  TIER="${spec%%:*}"
  REPO="${spec##*:}"
  echo ""
  echo "============================================================"
  echo "CHAIN: starting ${TIER}-${REPO} at $(date -u +%FT%TZ)"
  echo "============================================================"

  # Per-run pre-clean: ensure no stale release blocks the new helm install.
  helm uninstall hex-scaffold -n hex-scaffold --no-hooks 2>&1 | tail -2 || true
  kubectl delete job -n hex-scaffold hex-scaffold-migrate --ignore-not-found 2>&1 | tail -2 || true
  kubectl delete pod -n hex-scaffold -l job-name=hex-scaffold-migrate --ignore-not-found 2>&1 | tail -2 || true
  kubectl delete testrun -n testing-system "hex-scaffold-kafka-loadtest-$([ "$REPO" = "postgres" ] && echo pg || echo mongo)" --ignore-not-found 2>&1 | tail -2 || true

  # Run.
  bash "$DRIVER" "$TIER" "$REPO"
  RC=$?
  echo "CHAIN: ${TIER}-${REPO} ended rc=${RC} at $(date -u +%FT%TZ)"

  if [ $RC -ne 0 ]; then
    echo "CHAIN: ABORT — driver returned non-zero. Remaining runs skipped."
    exit $RC
  fi
done

echo ""
echo "CHAIN: all runs completed at $(date -u +%FT%TZ)"
