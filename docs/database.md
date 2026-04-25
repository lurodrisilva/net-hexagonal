# Database & Migration Strategy

## PostgreSQL schema

The `Samples` table (see [`20260411200621_InitialCreate.cs`](../src/Hex.Scaffold.Adapters.Persistence/PostgreSql/Migrations/20260411200621_InitialCreate.cs)) is the schema that every sample event and every REST payload must conform to:

| Column | Type | Null | Source of truth |
|--------|------|------|-----------------|
| `Id` | `integer` | no | `Sample.Id.Value` (Vogen value object) |
| `Name` | `varchar(200)` | no | `Sample.Name` (max-length validated by `SampleName` value object) |
| `Status` | `integer` | no | `SampleStatus` SmartEnum (1=Active, 2=Inactive, 3=NotSet) |
| `Description` | `varchar(1000)` | yes | optional free-form text |

The primary key (`Id`) is **application-assigned** (not `SERIAL`) so Kafka event replays land at deterministic ids. The `SampleByIdSpec` specification and all read paths assume this.

### Keeping samples aligned

- [`tests/loadtest/k6/rest-api-loadtest.js`](../tests/loadtest/k6/rest-api-loadtest.js) generates `Name` ≤ 200 chars and `Description` ≤ ~120 chars — both well inside the schema limits.
- [`tests/loadtest/kafka/sample-events.jsonl`](../tests/loadtest/kafka/sample-events.jsonl) contains only the three status values the SmartEnum knows about, and uses `null` descriptions to exercise the nullable column.
- [`tests/loadtest/kafka/load-test.sh synthetic`](../tests/loadtest/kafka/load-test.sh) generates names ≤ 80 chars, descriptions ≤ ~40 chars, and status ∈ {Active, Inactive, NotSet}.

## Migration strategy

This project uses **EF Core migrations** as the canonical schema change mechanism. The lifecycle:

1. **Author locally** when you change a domain entity or a `SampleConfiguration`:

   ```bash
   dotnet ef migrations add <Name> \
     --project src/Hex.Scaffold.Adapters.Persistence \
     --startup-project src/Hex.Scaffold.Api
   ```

   The new migration lands under `src/Hex.Scaffold.Adapters.Persistence/PostgreSql/Migrations/`. Commit it alongside the entity change.

2. **Apply locally** against a Dockerized Postgres:

   ```bash
   make db/migrate
   ```

   (Thin wrapper around `dotnet ef database update` — see the [`Makefile`](../Makefile).)

3. **Apply in-cluster** via the Helm **`pre-install` / `pre-upgrade` hook** Job rendered by [`migration-job.yaml`](../deploy/helm/hex-scaffold/templates/migration-job.yaml). The Job invokes `/app/efbundle` (a self-contained EF migration bundle baked into the runtime image at build time — see [Migration delivery: efbundle](#migration-delivery-efbundle-built-into-the-runtime-image)) with **no shell wrapper**, because the runtime image is chiseled (no `/bin/sh`, no `dotnet` CLI):

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

`dotnet ef migrations bundle` discovers the `DbContext` by bootstrapping the application host. The scaffold's host registers adapters via Scrutor by suffix (`*Service`, `*Repository`, `*Publisher`, `*Client`), which sweeps in `SampleReadModelRepository` (Mongo) **regardless** of the active feature selector. At design time the DI container then fails validation (`Unable to resolve service for type 'MongoDB.Driver.IMongoClient'`).

To bypass the host bootstrap, [`PostgreSql/DesignTimeDbContextFactory.cs`](../src/Hex.Scaffold.Adapters.Persistence/PostgreSql/DesignTimeDbContextFactory.cs) implements `IDesignTimeDbContextFactory<AppDbContext>`. EF picks it up first — no host, no Scrutor sweep, no MongoDB requirement. At runtime inside the cluster the same factory is invoked by the bundled binary; it reads `ConnectionStrings__PostgreSql` from the env (which Kubernetes populates from the Secret via `envFrom`).

#### Local migration authoring

When you author a new migration locally with `dotnet ef migrations add`, the same `DesignTimeDbContextFactory` is what EF uses — your shell environment doesn't need `ConnectionStrings__PostgreSql` set because EF only requires the factory to construct an in-memory `DbContext` for model diffing.

### MongoDB

Mongo is schemaless: the app upserts `SampleDocument` into a collection created on first write. No migration Job runs when `features.persistence=mongo`.
