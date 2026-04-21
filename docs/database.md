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

3. **Apply in-cluster** via the Helm **`pre-install` / `pre-upgrade` hook** Job rendered by [`migration-job.yaml`](../deploy/helm/hex-scaffold/templates/migration-job.yaml):

   - Runs exactly once per Helm release rollout, before the Deployment rolls.
   - Uses the `__EFMigrationsHistory` table for idempotency — re-applying a migration is a no-op.
   - Fails the release if the migration fails (`helm upgrade` returns non-zero), so you can't ship code that expects a schema that isn't there.

4. **Rollback** — two mechanisms, use the right one:

   | Situation | Action |
   |-----------|--------|
   | Code rolled but schema didn't | Re-run `helm upgrade` — hook retries |
   | Schema applied but code broken | `helm rollback` rolls the Deployment; the schema bump stays (that's what migrations are for) |
   | Bad migration landed | Author a **compensating** forward migration (`Remove-Migration` is not safe after a shared env has the old migration applied) |

### Production-grade: migration bundles

Running `dotnet ef` in-cluster requires the SDK image. For truly minimal prod images, publish a **migration bundle** and ship that:

```bash
dotnet ef migrations bundle \
  --project src/Hex.Scaffold.Adapters.Persistence \
  --startup-project src/Hex.Scaffold.Api \
  --output ./efbundle \
  --self-contained -r linux-x64
```

The Helm migration Job template already checks for `/app/efbundle` as a first preference — just add it to your migration image.

### MongoDB

Mongo is schemaless: the app upserts `SampleDocument` into a collection created on first write. No migration Job runs when `features.persistence=mongo`.
