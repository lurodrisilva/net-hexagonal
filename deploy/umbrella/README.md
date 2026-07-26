# hex-scaffold-umbrella — the golden-path deploy unit

Deploys **hex-scaffold together with its data infra** (Postgres from the SQL
building block) as **one versioned unit**, deployed by ArgoCD as a **single
`Application`**. This is the deploy shape decided in **ADR-0008** (umbrella chart
consumed unchanged by the orchestrator; the orchestrator resolves it by version
and supplies per-instance values — it does not author the chart).

## What it contains

| Dependency | Source | Role |
|------------|--------|------|
| `hex-scaffold` | `file://../helm/hex-scaffold` (this repo) | the application |
| `plat-eng-sql-database-package` (alias `sqldatabase`) | `oci://ghcr.io/lurodrisilva/helm-charts` | CloudNativePG `Cluster` + Secret |

Build the dependency tree before templating/deploying:

```sh
helm dependency build .        # pulls hex-scaffold (local) + the SQL building block (OCI)
```

## Ordering (ArgoCD sync-waves — one Application)

| Wave | Resource | Gate |
|------|----------|------|
| PreSync | hex-scaffold `Secret` + `ConfigMap` (Helm hooks) | land first |
| 0 | SQL building block → CNPG `Cluster <name>` | **health-gated** by ArgoCD's built-in `postgresql.cnpg.io/Cluster` check |
| 1 | hex-scaffold migration `Job` (ArgoCD `Sync` hook) | runs only after the Cluster is `Ready` |
| 2 | hex-scaffold `Deployment` | rolls after migrations |

`hex-scaffold` is switched into sync-wave ordering by `app.argo.enabled=true`
(see the app chart's `values.yaml`). With it **off** (the default) the app chart
still deploys standalone with plain Helm pre-install hooks — the two modes
coexist.

The Postgres binding reuses `hex-scaffold`'s `postgres.bindBuildingBlock` (G-T1):
credentials flow only via `secretKeyRef` off `<name>-secret`, composed with
Kubernetes `$(VAR)` expansion — **no plaintext credential renders**.

## One knob

`values.yaml` centres on the **app/database name** (example `acct`). It names the
CNPG `Cluster`, its `Secret` (`<name>-secret`), its database (`<name>-db`), and
the read-write `Service` the operator creates (`<name>-rw`). The `hex-scaffold`
bind block must point at those exact names — keep them in sync.

## Cluster prerequisites (not provided by this chart)

- **CloudNativePG operator** installed (a baseline addon).
- A cluster-level **`superuser-secret`** the SQL building block's `Cluster`
  references (`spec.superuserSecret`).
- **ArgoCD** with its built-in resource health checks (ships the
  `postgresql.cnpg.io/Cluster` assessment — no custom health-lua needed).
- Redis is **off** for v1 (`features.redis=false`); the cache building block is
  Azure-managed and deferred to Day-2 (G-T3 / G-T2).

## Deploy as an ArgoCD Application

```yaml
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: acct
  namespace: argocd
spec:
  project: <argocd-project>
  source:
    repoURL: https://github.com/lurodrisilva/net-hexagonal
    targetRevision: <git-ref>
    path: deploy/umbrella
    helm:
      valuesObject:
        sqldatabase:
          databases:
            sql:
              - { name: acct, user: acct_user, password: <from-external-secret>, instances: 1, storage: { size: 5Gi } }
        hex-scaffold:
          postgres:
            bindBuildingBlock: { enabled: true, secretName: acct-secret, host: acct-rw, database: acct-db }
  destination:
    server: https://kubernetes.default.svc
    namespace: <ns>
  syncPolicy:
    automated: { prune: true, selfHeal: true }
    syncOptions: [ CreateNamespace=true ]
```

## Validation status

- `helm dependency build` / `helm lint` / `helm template` — **clean** (rendered
  `Cluster` + `Secret`; app + migration bound via `secretKeyRef`; migration =
  `Sync` hook wave 1; app = wave 2; no plaintext, no external-PG default).
- **Cluster-apply: pending** — requires a bootstrapped AKS cluster with ArgoCD +
  the CloudNativePG operator + the `superuser-secret`. Not yet run.
