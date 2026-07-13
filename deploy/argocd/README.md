# Golden-path deployment: app + infra together

`hex-scaffold`'s chart deploys the **application only** and, by default, expects
a Postgres to already exist (`secrets.postgresConnectionString` points at an
externally-provisioned server). On the Azure Platform Engineering paved road,
data infra is instead provisioned **on the same cluster** by the SQL building
block (CloudNativePG). This directory shows how to deliver both **together**.

## The `postgres.bindBuildingBlock` chart mode

Enabling `postgres.bindBuildingBlock` rewires the chart to bind to the SQL
building block's outputs instead of a literal connection string:

```yaml
postgres:
  bindBuildingBlock:
    enabled: true
    secretName: acct-secret   # the building block's <name>-secret (keys: username, password)
    host: acct-rw             # the CNPG read-write service
    database: acct-db
    port: 5432
```

When enabled, both the Deployment and the migration Job read `PGUSER` /
`PGPASSWORD` from `<name>-secret` via `secretKeyRef` and compose
`ConnectionStrings__PostgreSql` with Kubernetes `$(VAR)` expansion. **No
plaintext database credential is templated into any rendered manifest** — the
literal `ConnectionStrings__PostgreSql` Secret key is skipped, and an explicit
container `env` (which overrides `envFrom` on key collision) supplies the
composed string. The standalone/demo path is unchanged when the flag is off.

## Two-wave app-of-apps

`app-of-apps.example.yaml` (illustrative — replace the `<...>` placeholders)
provisions Postgres and deploys the bound app in dependency order:

| Wave | Application | Provisions |
|------|-------------|------------|
| 1 | `acct-sql` | SQL building block → CNPG `Cluster acct` (svc `acct-rw`, db `acct-db`, secret `acct-secret`) |
| 2 | `acct-app` | migration Job (pre-install hook) + `hex-scaffold` Deployment, bound to wave 1 |

**Prerequisite:** ArgoCD needs a health assessment for the CNPG `Cluster` CR so
wave 2 waits for Postgres to be `Ready`; otherwise the wave-2 migration Job
races the database (mitigated, not eliminated, by the Job's `backoffLimit`).

Redis is intentionally off for v1 — the cache building block is Azure-managed
Redis (async provisioning + cost), deferred to Day-2.
