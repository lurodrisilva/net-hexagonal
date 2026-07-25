#!/usr/bin/env bash
#
# scaffold-render.sh — the ADR-0009 golden-path "create" render engine.
#
# Renders THIS net-hexagonal template (the concrete "Hex.Scaffold" service) into
# a new application by copying tracked content and substituting the template
# identity tokens with the requested app name. It is the testable core invoked
# by .github/workflows/scaffold-new-app.yml; it has no GitHub/network deps so it
# can be run and asserted locally.
#
# Usage:
#   scaffold-render.sh <src-dir> <dest-dir> <AppNamePascal> <app-name-kebab>
#
#   src-dir        root of the template checkout (a git work tree, e.g. $GITHUB_WORKSPACE)
#   dest-dir       empty/new directory to render into (created if missing)
#   AppNamePascal  PascalCase identity, e.g. "Orders" or "MySvc" (namespaces / sln / csproj)
#   app-name-kebab kebab-case identity, e.g. "orders" or "my-svc" (helm / chart / k8s names)
#                  Capped at 50 chars: Azure caps Flexible Server names at 63 and
#                  the platform appends a 13-char uniquifier.
#
# Optional environment (unset = keep the template's value, so the platform
# default lives in exactly one place — deploy/umbrella/values.yaml):
#   DB_SIZE         small | medium | large
#   DB_VERSION      PostgreSQL major, e.g. "16"
#   DB_STORAGE_MB   provisioned storage in MB
#
# What it does:
#   1. Copies the source's *tracked* content into <dest-dir>, EXCLUDING:
#        - .git (never listed by git ls-files anyway)
#        - .github/workflows/scaffold-new-app.yml  (don't propagate the scaffolder
#          into scaffolded apps)
#        - build output (bin/ obj/)  (defensive; also gitignored)
#   2. Substitutes file CONTENTS across text files (binaries skipped):
#        HexScaffold  -> <AppNamePascal>   (dot-free single-token usages, if any)
#        Hex.Scaffold -> <AppNamePascal>   (root namespace / assembly / sln / csproj)
#        hex-scaffold -> <app-name-kebab>  (helm chart / k8s / template helper names)
#   3. Renames files/dirs whose PATH contains those tokens (bottom-up), e.g.
#        deploy/helm/hex-scaffold/          -> deploy/helm/<app-name-kebab>/
#        Hex.Scaffold.slnx                  -> <AppNamePascal>.slnx
#        src/Hex.Scaffold.Domain/...csproj  -> src/<AppNamePascal>.Domain/...csproj
#   4. Names the app's DATABASE after the app, in deploy/umbrella/values.yaml:
#        databases.sql[0].name             -> <app-name-kebab>
#        bindBuildingBlock.instanceName    -> <app-name-kebab>
#        azureFlexibleServer.databaseName  -> dropped (the building block derives
#                                             <name>-db from the instance name)
#      The identity tokens in step 2 cannot reach this: the umbrella ships the
#      template's sample instance "acct", so without this every scaffolded app
#      provisions an Azure server called acct-<random> — no ownership, no cost
#      attribution, and two apps' servers indistinguishable in the portal.
#      "acct" is four characters and appears in prose, so these edits are
#      key-anchored rather than token substitutions, and each is verified after
#      the fact: the failure worth guarding against is a silent no-op that ships
#      the sample name under a green build.
#
# DELIBERATELY NOT DONE (documented follow-up):
#   The sample DOMAIN word "account" (AccountAggregate, AccountBatchProcessor,
#   /accounts endpoints, etc.) is INTENTIONALLY left intact. A blind rename of
#   "account" across the sample code is too risky (it collides with unrelated
#   English prose and would corrupt working reference code), so the scaffolded
#   app keeps the reference "account" domain. A real domain-rename belongs to a
#   template engine with an explicit domain model — tracked as a follow-up, see
#   docs/scaffolder.md. This render is intentionally heuristic (sed-based).
#
set -euo pipefail

usage() {
  echo "usage: $0 <src-dir> <dest-dir> <AppNamePascal> <app-name-kebab>" >&2
  exit 2
}

[ "$#" -eq 4 ] || usage

SRC="$1"
DEST="$2"
APP_PASCAL="$3"
APP_KEBAB="$4"

[ -d "$SRC" ] || { echo "error: src-dir '$SRC' is not a directory" >&2; exit 1; }

# Validate the substitution values so they can never inject sed/regex or path
# metacharacters. PascalCase must be a bare alphanumeric token; kebab is
# lowercase alphanumeric with internal dashes.
case "$APP_PASCAL" in
  *[!A-Za-z0-9]*|"") echo "error: AppNamePascal must be alphanumeric, got '$APP_PASCAL'" >&2; exit 1 ;;
esac
case "$APP_KEBAB" in
  *[!a-z0-9-]*|""|-*|*-) echo "error: app-name-kebab must be lowercase [a-z0-9-] (no leading/trailing dash), got '$APP_KEBAB'" >&2; exit 1 ;;
esac

# Resolve SRC to an absolute path so `git -C` and copies are unambiguous.
SRC="$(cd "$SRC" && pwd)"

mkdir -p "$DEST"

# ---------------------------------------------------------------------------
# 1. Enumerate + copy tracked content (preserving original paths for now).
# ---------------------------------------------------------------------------
# Prefer git's notion of tracked files (excludes .git and everything gitignored,
# e.g. bin/obj). Fall back to a find-based sweep if SRC is not a git work tree.
enumerate() {
  if git -C "$SRC" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    git -C "$SRC" ls-files -z
  else
    ( cd "$SRC" && find . -type f -not -path './.git/*' -print0 | sed -e 's|\./||g' )
  fi
}

is_excluded() {
  # $1 = repo-relative path
  case "$1" in
    .github/workflows/scaffold-new-app.yml) return 0 ;;
    bin/*|obj/*|*/bin/*|*/obj/*)            return 0 ;;
    *) return 1 ;;
  esac
}

copied=0
while IFS= read -r -d '' rel; do
  [ -n "$rel" ] || continue
  if is_excluded "$rel"; then continue; fi
  mkdir -p "$DEST/$(dirname "$rel")"
  cp "$SRC/$rel" "$DEST/$rel"
  copied=$((copied + 1))
done < <(enumerate)

# ---------------------------------------------------------------------------
# 2. Substitute file CONTENTS across text files (skip binaries).
# ---------------------------------------------------------------------------
# `grep -Iq .` succeeds only for non-empty text files (-I => treat binary as no
# match), so we never run sed over binary assets. The three token patterns are
# disjoint (no-dot PascalCase / dotted PascalCase / lowercase-kebab), so replace
# order is irrelevant. The dot in Hex.Scaffold is escaped to stay literal.
files_changed=0
subs_total=0
while IFS= read -r -d '' f; do
  if grep -Iq . "$f" 2>/dev/null; then
    # `|| true` keeps a no-match (grep exit 1) from tripping set -e / pipefail.
    n=$( { grep -oE 'HexScaffold|Hex\.Scaffold|hex-scaffold' "$f" 2>/dev/null || true; } | wc -l | tr -d '[:space:]')
    if [ "${n:-0}" -gt 0 ]; then
      sed -i.bak \
        -e "s/HexScaffold/${APP_PASCAL}/g" \
        -e "s/Hex\.Scaffold/${APP_PASCAL}/g" \
        -e "s/hex-scaffold/${APP_KEBAB}/g" \
        "$f"
      rm -f "$f.bak"
      files_changed=$((files_changed + 1))
      subs_total=$((subs_total + n))
    fi
  fi
done < <(find "$DEST" -type f -print0)

# ---------------------------------------------------------------------------
# 3. Rename PATHS containing the tokens (bottom-up via -depth so a parent dir is
#    renamed only after all its children have already been emitted/renamed).
# ---------------------------------------------------------------------------
paths_renamed=0
while IFS= read -r -d '' p; do
  base="$(basename "$p")"
  dir="$(dirname "$p")"
  newbase="$(printf '%s' "$base" | sed \
    -e "s/HexScaffold/${APP_PASCAL}/g" \
    -e "s/Hex\.Scaffold/${APP_PASCAL}/g" \
    -e "s/hex-scaffold/${APP_KEBAB}/g")"
  if [ "$newbase" != "$base" ]; then
    mv "$p" "$dir/$newbase"
    paths_renamed=$((paths_renamed + 1))
  fi
done < <(find "$DEST" -depth \( -name '*hex-scaffold*' -o -name '*Hex.Scaffold*' -o -name '*HexScaffold*' \) -print0)

# ---------------------------------------------------------------------------
# 4. Name the app's database after the app.
# ---------------------------------------------------------------------------
# The identity tokens above are PascalCase/kebab and cannot reach the database
# name: the umbrella ships the template's sample instance, "acct". Left alone,
# every scaffolded app provisions an Azure server called acct-<random> — no
# ownership, no cost attribution, and two apps' servers indistinguishable in the
# portal. (Observed live on aks-test: orders-v4 was running against
# acct-6584520516bb.)
#
# "acct" is four characters and appears in prose throughout this file, so a token
# substitution like the ones above is not safe here. Each edit is anchored to its
# key, and the post-conditions below fail the render if an anchor stopped
# matching — the failure mode worth guarding is not a bad rewrite but a silent
# no-op that ships the sample name under a green build.
UMBRELLA_VALUES="$DEST/deploy/umbrella/values.yaml"
if [ -f "$UMBRELLA_VALUES" ]; then
  # Azure Flexible Server names are capped at 63 characters and the Composition
  # appends a 13-character uniquifier (observed: acct-78f5fa105d75). Refuse here
  # rather than let Crossplane reject the server ~10 minutes into a deploy.
  if [ "${#APP_KEBAB}" -gt 50 ]; then
    echo "error: app-name-kebab '$APP_KEBAB' is ${#APP_KEBAB} chars; max 50" >&2
    echo "       (Azure caps server names at 63 and the platform adds a 13-char suffix)" >&2
    exit 1
  fi

  # The name line carries a trailing comment naming the Secrets it produces. It
  # is replaced wholesale rather than patched: a comment left saying "XR acct"
  # beside `name: orders-v4` would ship a scaffolded repo whose documentation
  # contradicts its own config, which is the exact class of staleness that made
  # this bug expensive to find.
  sed -i.bak \
    -e "s|^      - name: acct.*$|      - name: ${APP_KEBAB}               # -> XR ${APP_KEBAB}, Secrets ${APP_KEBAB}-postgres-conn + ${APP_KEBAB}-admin-password|" \
    -e "s|^\(      instanceName: \)acct$|\1${APP_KEBAB}|" \
    -e "/^          databaseName: acct-db$/d" \
    "$UMBRELLA_VALUES"
  rm -f "$UMBRELLA_VALUES.bak"

  # Post-conditions. Both keys must now carry the app name, and the dropped
  # databaseName must be gone — the building block defaults it to <name>-db, so
  # deleting it removes a restatement instead of renaming one. Values are read
  # back with trailing comments and whitespace stripped.
  strip() { sed -e 's|[[:space:]]*#.*$||' -e 's|[[:space:]]*$||'; }
  db_name=$(sed -n 's|^      - name: ||p' "$UMBRELLA_VALUES" | head -1 | strip)
  bind_name=$(sed -n 's|^      instanceName: ||p' "$UMBRELLA_VALUES" | head -1 | strip)
  for pair in "databases.sql[0].name:${db_name}" "bindBuildingBlock.instanceName:${bind_name}"; do
    field="${pair%%:*}"; got="${pair#*:}"
    if [ "$got" != "$APP_KEBAB" ]; then
      echo "error: ${field} is '${got}', expected '${APP_KEBAB}'" >&2
      echo "       the umbrella layout changed and this rewrite silently missed it" >&2
      exit 1
    fi
  done
  if grep -q '^          databaseName:' "$UMBRELLA_VALUES"; then
    echo "error: databaseName survived the rewrite; it must be dropped so the" >&2
    echo "       building block derives <name>-db from the instance name" >&2
    exit 1
  fi
  echo "scaffold-render: database named '${APP_KEBAB}' (XR + bind)"

  # Optional sizing, passed through from the scaffold workflow's inputs. Unset
  # leaves the template's value, so the platform default stays in one place.
  for spec in "size:${DB_SIZE:-}" "version:${DB_VERSION:-}" "storageMb:${DB_STORAGE_MB:-}"; do
    key="${spec%%:*}"; val="${spec#*:}"
    [ -z "$val" ] && continue
    case "$key" in
      version) repl="\"${val}\"" ;;   # the XRD types version as a string
      *)       repl="${val}" ;;
    esac
    sed -i.bak -e "s|^\(          ${key}: \).*$|\1${repl}|" "$UMBRELLA_VALUES"
    rm -f "$UMBRELLA_VALUES.bak"
    grep -q "^          ${key}: ${repl}$" "$UMBRELLA_VALUES" \
      || { echo "error: failed to set azureFlexibleServer.${key}=${val}" >&2; exit 1; }
    echo "scaffold-render: azureFlexibleServer.${key}=${val}"
  done
else
  # Not fatal: the template could legitimately be rendered without the umbrella
  # (a chart-only consumer), and failing would break that. Say so loudly instead
  # of leaving the caller to infer it from a database that never appears.
  echo "scaffold-render: WARNING no deploy/umbrella/values.yaml — database not named" >&2
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo "scaffold-render: rendered '${APP_PASCAL}' / '${APP_KEBAB}'"
echo "  source            : ${SRC}"
echo "  destination       : ${DEST}"
echo "  files copied      : ${copied}"
echo "  files substituted : ${files_changed}"
echo "  substitutions     : ${subs_total}"
echo "  paths renamed     : ${paths_renamed}"
