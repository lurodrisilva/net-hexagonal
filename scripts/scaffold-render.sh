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
# Summary
# ---------------------------------------------------------------------------
echo "scaffold-render: rendered '${APP_PASCAL}' / '${APP_KEBAB}'"
echo "  source            : ${SRC}"
echo "  destination       : ${DEST}"
echo "  files copied      : ${copied}"
echo "  files substituted : ${files_changed}"
echo "  substitutions     : ${subs_total}"
echo "  paths renamed     : ${paths_renamed}"
