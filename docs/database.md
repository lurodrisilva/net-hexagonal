# Database & Migration Strategy

## PostgreSQL schema

The `accounts` table reproduces Stripe's v2 `v2.core.account` shape — top-level scalars as proper columns, nested objects as `jsonb`. See the regenerated InitialCreate migration under [`PostgreSql/Migrations/`](../src/Hex.Scaffold.Adapters.Persistence/PostgreSql/Migrations) for the canonical DDL.

| Column | Type | Null | Source of truth |
|---|---|---|---|
| `id` | `varchar(64)` PK | no | `AccountId.Value` (Vogen string-typed value object, `acct_…` prefix) |
| `livemode` | `boolean` | no | `Account.Livemode` |
| `created` | `timestamptz` | no | `Account.Created` (UTC) |
| `closed` | `boolean` | no | `Account.Closed` |
| `display_name` | `varchar(200)` | yes | `Account.DisplayName` |
| `contact_email` | `varchar(254)` | yes | `Account.ContactEmail` |
| `contact_phone` | `varchar(40)` | yes | `Account.ContactPhone` |
| `dashboard` | `varchar(16)` | yes | `Account.Dashboard` (`full` \| `express` \| `none`) |
| `applied_configurations` | `text[]` | no | `Account.AppliedConfigurations` (native Postgres array; queryable via `ANY`) |
| `configuration` | `jsonb` | yes | `Account.ConfigurationJson` — Stripe's customer/merchant/recipient block |
| `identity` | `jsonb` | yes | `Account.IdentityJson` — country, entity_type, business_details, individual |
| `defaults` | `jsonb` | yes | `Account.DefaultsJson` — currency, locales, profile, responsibilities |
| `requirements` | `jsonb` | yes | `Account.RequirementsJson` |
| `future_requirements` | `jsonb` | yes | `Account.FutureRequirementsJson` |
| `metadata` | `jsonb` | yes | `Account.MetadataJson` — caller-supplied free-form |

Index: `ix_accounts_created_id_desc` on `(created DESC, id DESC)` — supports the cursor-paginated list path without a full sort.

The primary key is **application-assigned** (`acct_<22-char>` random) — `Account.Create()` generates the ID inside the domain before EF's `IdentityMap.Add` ever sees the entity. There is no Hi-Lo sequence and no `IAccountIdGenerator` port; the design lesson came from the Vogen + EF key-hashing journey that the earlier Sample aggregate took.

### Dynamic JSON requirement (Npgsql)

The `string` ↔ `jsonb` round-trip on the six nested-blob columns requires Npgsql's dynamic-JSON mapping. [`PostgreSqlServiceExtensions`](../src/Hex.Scaffold.Adapters.Persistence/Extensions/PostgreSqlServiceExtensions.cs) builds an `NpgsqlDataSource` with `EnableDynamicJson()`:

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();
```

Without that flag Npgsql rejects raw strings against `jsonb` parameters at write time.

### Keeping load test data aligned

- [`tests/loadtest/k6/rest-api-loadtest.js`](../tests/loadtest/k6/rest-api-loadtest.js) generates payloads that satisfy the schema:
  - `display_name` ≤ 200 chars
  - `contact_email` ≤ 254 chars, valid format
  - `applied_configurations[*]` ∈ `{customer, merchant, recipient}`
  - `identity` / `configuration` / `metadata` are well-formed JSON objects

The Kafka load-test deck under `tests/loadtest/kafka/` still reflects the prior Sample event shape and has not been migrated to Account events. It's parked until the inbound Kafka consumer needs it.

## Migration strategy

This project uses **EF Core migrations** as the canonical schema change mechanism. The lifecycle:

1. **Author locally** when you change the aggregate or `AccountConfiguration`:

   ```bash
   dotnet ef migrations add <Name> \
     --project src/Hex.Scaffold.Adapters.Persistence \
     --startup-project src/Hex.Scaffold.Api
   ```

   The new migration lands under `src/Hex.Scaffold.Adapters.Persistence/PostgreSql/Migrations/`. Commit it alongside the entity change. `dotnet-ef` is pinned in [`.config/dotnet-tools.json`](../.config/dotnet-tools.json) so `dotnet tool restore` once and the local tool is available repo-wide.

2. **Apply locally** against a Dockerized Postgres:

   ```bash
   make db/migrate
   ```

   (Thin wrapper around `dotnet ef database update` — see the [`Makefile`](../Makefile).)

3. **Apply in-cluster** via the Helm **`pre-install` / `pre-upgrade` hook** Job rendered by [`migration-job.yaml`](../deploy/helm/hex-scaffold/templates/migration-job.yaml). The Job invokes `/app/efbundle` (a self-contained EF migration bundle baked into the runtime image at build time) with **no shell wrapper**, because the runtime image is chiseled (no `/bin/sh`, no `dotnet` CLI):

   - Runs exactly once per Helm release rollout, before the Deployment rolls.
   - Uses the `__EFMigrationsHistory` table for idempotency — re-applying a migration is a no-op.
   - Reads `ConnectionStrings__PostgreSql` from the `envFrom` Secret rather than the command line, so the connection string never leaks into `ps`.
   - Fails the release if the migration fails (`helm upgrade` returns non-zero), so you can't ship code that expects a schema that isn't there.

   Hook ordering: the runtime `ConfigMap` and `Secret` are also rendered as `pre-install,pre-upgrade` hooks at weight `-10`, so they exist before the migration Job (weight `-5`) starts. `before-hook-creation` keeps them in the cluster after the hook completes so the regular Deployment can mount them.

4. **Rollback** — two mechanisms, use the right one:

   | Situation | Action |
   |-----------|--------|
   | Code rolled but schema didn't | Re-run `helm upgrade` — hook retries |
   | Schema applied but code broken | `helm rollback` rolls the Deployment; the schema bump stays (that's what migrations are for) |
   | Bad migration landed | Author a **compensating** forward migration (`Remove-Migration` is not safe after a shared env has the old migration applied) |

### Migration delivery: efbundle (built into the runtime image)

The runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`) is **chiseled**: no shell, no package manager, no .NET SDK. Running `dotnet ef database update` in the pod is therefore impossible. The scaffold solves this once, at build time:

1. The Dockerfile's build stage (after `dotnet publish`) installs the `dotnet-ef` global tool and runs:

   ```dockerfile
   /dotnet-tools/dotnet-ef migrations bundle \
       --project src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj \
       --self-contained \
       --target-runtime $RID \
       --output /efbundle/efbundle \
       --configuration Release
   ```

   `$RID` is mapped from `$TARGETARCH` (`amd64` → `linux-x64`, `arm64` → `linux-arm64`), so multi-arch builds produce one bundle per platform.

2. The final stage `COPY --from=build /efbundle/efbundle /app/efbundle`. The bundle is a self-contained executable that embeds the migrations + a minimal .NET runtime — no system dependencies, no shell needed.

3. The Helm migration Job invokes it directly:

   ```yaml
   command: ["/app/efbundle"]
   envFrom:
     - configMapRef: { name: hex-scaffold }
     - secretRef:    { name: hex-scaffold }
   ```

#### Why a `DesignTimeDbContextFactory` is required

`dotnet ef migrations bundle` discovers the `DbContext` by bootstrapping the application host. The scaffold's host registers adapters via Scrutor by suffix (`*Service`, `*Repository`, `*Publisher`, `*Client`) which can sweep in services regardless of the active feature selector. At design time the DI container can then fail validation on missing dependencies (e.g. an `IConnectionMultiplexer` for Redis when none is configured).

To bypass the host bootstrap, [`PostgreSql/DesignTimeDbContextFactory.cs`](../src/Hex.Scaffold.Adapters.Persistence/PostgreSql/DesignTimeDbContextFactory.cs) implements `IDesignTimeDbContextFactory<AppDbContext>`. EF picks it up first — no host, no Scrutor sweep. At runtime inside the cluster the same factory is invoked by the bundled binary; it reads `ConnectionStrings__PostgreSql` from the env (which Kubernetes populates from the Secret via `envFrom`).

#### Local migration authoring

When you author a new migration locally with `dotnet ef migrations add`, the same `DesignTimeDbContextFactory` is what EF uses — your shell environment doesn't need `ConnectionStrings__PostgreSql` set because EF only requires the factory to construct an in-memory `DbContext` for model diffing.

### Upgrading from earlier scaffold revisions

The scaffold previously shipped a `Samples` table (with a `samples_hilo_seq` sequence). The Account replacement deletes those migrations and ships a fresh `InitialCreate` for the `accounts` table. Pre-existing demo deployments need `helm uninstall hex-scaffold` before upgrading, since the new migration history conflicts with the old `__EFMigrationsHistory` rows. New clusters install cleanly.

### MongoDB

Mongo currently has no read-model wired — the `IMongoClient` registration is preserved for the `features.persistence=mongo` selector path, but no `IAccountReadModelRepository` exists. A future PR can drop one in without changing the registration shape. No migration Job runs when `features.persistence=mongo`; Mongo is schemaless and collections appear on first write.
