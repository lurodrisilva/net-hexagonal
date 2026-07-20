# Scaffolder — the ADR-0009 "create a new app" workflow

This repo is not only a runnable service; it is the **golden-path template**. The
[`.github/workflows/scaffold-new-app.yml`](../.github/workflows/scaffold-new-app.yml)
workflow is the **ADR-0009 scaffolder** — the "create" half of the golden path.
When dispatched, it renders **this** template (the concrete `Hex.Scaffold`
service) into a **brand-new GitHub repository** for a requested application.

It is kept **outside** the platform orchestrator on purpose (ADR-0009): the
orchestrator owns the app lifecycle, but repo creation happens on GitHub Actions
so the orchestrator never needs Git write credentials or a working tree.

## How it is triggered

The orchestrator's **`POST /api/v1/apps`** dispatches this `workflow_dispatch`
with the requested app identity. Inputs:

| Input         | Required | Default        | Purpose                                                        |
|---------------|----------|----------------|----------------------------------------------------------------|
| `appName`     | yes      | —              | New app name (e.g. `orders`, `my-svc`). Drives the identities. |
| `owner`       | no       | `lurodrisilva` | GitHub user/org that owns the new repo.                        |
| `domain`      | no       | `account`      | Reserved for the future domain-rename (see Limitations).       |
| `description` | no       | `""`           | Description for the new repository.                             |

## What it does

1. Checks out this template.
2. Mints a short-lived **GitHub App** installation token scoped to `owner`.
3. Computes two identities from `appName`:
   - **PascalCase** (`orders` → `Orders`, `my-svc` → `MySvc`) — namespaces, `.slnx`, `.csproj`.
   - **kebab-case** (`orders`, `my-svc`) — Helm/chart/k8s names **and** the new repo name.
4. Creates `owner/<kebab>` as a **private** repo (idempotent — if it already exists it just re-pushes).
5. Runs [`scripts/scaffold-render.sh`](../scripts/scaffold-render.sh) to render this
   template into a temp dir with the new identity.
6. `git init` a fresh history in the rendered tree, commits as the scaffolder bot,
   and pushes to `owner/<kebab>`.
7. Reports the new repo URL.

## Authentication — the GitHub App

Both the workflow and the orchestrator use the **same GitHub App**. Its
credentials live in this repo's **Actions secrets**:

- `SCAFFOLDER_APP_ID` — the App's numeric app id.
- `SCAFFOLDER_APP_PRIVATE_KEY` — the App's PEM private key.

The App must be installed on `owner` with permission to **create repositories**
and **push code**. This is the same App the orchestrator authenticates as to
dispatch this workflow — documented in the harness **Phase F** proof doc.

## The render engine

[`scripts/scaffold-render.sh`](../scripts/scaffold-render.sh) is the testable
core (no GitHub/network dependency), so it can be run and asserted locally:

```bash
scripts/scaffold-render.sh . /tmp/orders Orders orders
# asserts: /tmp/orders/Orders.slnx, /tmp/orders/deploy/helm/orders/Chart.yaml,
# and zero remaining "Hex.Scaffold"/"hex-scaffold" tokens in the tree.
```

It (1) copies tracked content (excluding `.git`, the scaffolder workflow itself,
and `bin/obj`), (2) substitutes file contents
(`HexScaffold`/`Hex.Scaffold` → PascalCase, `hex-scaffold` → kebab, over text
files only), and (3) renames matching file/dir paths.

## Limitations (documented)

The render is **heuristic, sed-based** — a deliberately simple token substitution,
not a real template engine. In particular:

- **The sample domain word `account` is left intact.** A blind rename of
  `account` across the reference code (`AccountAggregate`, `AccountBatchProcessor`,
  `/accounts`, …) is too risky — it collides with English prose and would corrupt
  working reference code. The scaffolded app therefore keeps the reference
  `account` domain. The `domain` workflow input is accepted and logged today but
  does **not** yet drive substitution.
- **A production scaffolder** would use a proper template engine (parameterized
  placeholders, an explicit project model) plus a real **domain rename** driven
  by the `domain` input. That is the intended follow-up to this ADR-0009
  minimal implementation.
