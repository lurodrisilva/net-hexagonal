# Development guide

This page is for getting the scaffold running locally and for day-to-day work on a feature.

## Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0.100 (see [`global.json`](../global.json) — `rollForward: latestMajor`, prerelease allowed) |
| Docker | Any recent version (needed for integration tests and local infra) |
| `dotnet-ef` | Matching EF Core 10 (`dotnet tool install --global dotnet-ef --version 10.*`) |

## Infrastructure

The API talks to four backends. Run them however you like — the simplest path is a Docker Compose stack (not committed — the team uses local installs or short-lived containers). Minimum:

| Service | Default connection |
|---|---|
| PostgreSQL | `Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=hex-scaffold` |
| MongoDB | `mongodb://localhost:27017`, db `hex-scaffold` |
| Redis | `localhost:6379` |
| Kafka | `localhost:9092` |

A minimal one-shot docker command set (adapt as needed):

```bash
docker run -d --name hex-postgres -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16
docker run -d --name hex-redis -p 6379:6379 redis:7-alpine
docker run -d --name hex-mongo -p 27017:27017 mongo:7
# Kafka is heavier — use Confluent or Redpanda. Example with Redpanda:
docker run -d --name hex-kafka -p 9092:9092 redpandadata/redpanda:latest \
  redpanda start --overprovisioned --smp 1 --memory 1G --reserve-memory 0M --node-id 0 --check=false \
  --advertise-kafka-addr PLAINTEXT://localhost:9092
```

The API degrades gracefully without Kafka (readiness returns `Degraded`, not `Unhealthy`). It will **not** start cleanly without Postgres (migration on startup).

## Build & run

```bash
dotnet build
dotnet run --project src/Hex.Scaffold.Api
```

The service listens on `http://localhost:8080`. In development, migrations are applied automatically (`ApplyMigrationsOnStartup: true` in `appsettings.Development.json`).

Check it is up:

```bash
curl http://localhost:8080/healthz
curl http://localhost:8080/ready
open http://localhost:8080/scalar/v1   # macOS
```

## Configuration

[`appsettings.json`](../src/Hex.Scaffold.Api/appsettings.json) is the base. `appsettings.Development.json` overrides for local dev. `appsettings.Testing.json` covers `Testing` env.

Override with environment variables or user-secrets:

```bash
export ConnectionStrings__PostgreSql='Host=localhost;Database=hex;Username=postgres;Password=postgres'
export Redis__ConnectionString=localhost:6379
export Kafka__BootstrapServers=localhost:9092
```

## EF Core migrations

Create a new migration:

```bash
dotnet ef migrations add <Name> \
  --project src/Hex.Scaffold.Adapters.Persistence \
  --startup-project src/Hex.Scaffold.Api
```

Apply manually (instead of startup):

```bash
dotnet ef database update \
  --project src/Hex.Scaffold.Adapters.Persistence \
  --startup-project src/Hex.Scaffold.Api
```

Remove the last migration (only if not applied):

```bash
dotnet ef migrations remove \
  --project src/Hex.Scaffold.Adapters.Persistence \
  --startup-project src/Hex.Scaffold.Api
```

The existing initial migration is [`20260411200621_InitialCreate`](../src/Hex.Scaffold.Adapters.Persistence/PostgreSql/Migrations/20260411200621_InitialCreate.cs).

## Adding a new use case

The canonical workflow — adapt to your aggregate:

1. **Domain** — add/modify aggregate methods on the relevant aggregate root. Register domain events if state changed meaningfully.
2. **Application** — create a folder `Application/<Aggregate>/<Operation>/` containing:
   - `<Operation>Command.cs` or `<Operation>Query.cs` implementing `ICommand<T>` / `IQuery<T>`
   - `<Operation>Handler.cs` implementing `ICommandHandler<,>` / `IQueryHandler<,>`
   - Feature-scoped port interface if the handler needs one (e.g. a Dapper query service)
3. **Adapter** — implement the port if new (Persistence/Outbound). Register it in the relevant `*ServiceExtensions` — or let Scrutor pick it up if the name ends in `Service`/`Repository`/`Publisher`/`Client`.
4. **Inbound** — add a FastEndpoint in `Adapters.Inbound/Api/<Aggregate>/<Operation>.cs`. Use `ResultExtensions` to map `Result` to typed HTTP results. Add a `Validator<TRequest>`.
5. **Tests** — add unit test(s) for the handler and aggregate methods; add integration test(s) if a new adapter was introduced.
6. **Architecture** — check `dotnet test --filter Category=Architecture` still passes.

See [`application.md`](application.md) for handler patterns and [`adapters.md`](adapters.md) for adapter patterns.

## Adding a new aggregate

1. Create `Domain/<Aggregate>/` with the root (`EntityBase<Root, RootId>` + `IAggregateRoot`), value objects (Vogen), SmartEnum status if any, events, specs.
2. Add EF config in `Adapters.Persistence/PostgreSql/Config/<Aggregate>Configuration.cs`.
3. Add `DbSet<Root> <Aggregates> => Set<Root>();` in `AppDbContext`.
4. Create an EF migration.
5. Follow the use-case workflow above.

Architecture tests will catch boundary violations.

## Repo conventions

- `.editorconfig` is authoritative for style (2-space indent, expression-bodied members preferred).
- Warnings are errors (`TreatWarningsAsErrors = true` in [`Directory.Build.props`](../Directory.Build.props)).
- Nullable reference types enabled everywhere.
- Global usings live in each project's `GlobalUsings.cs`.
- Central package versions are in [`Directory.Packages.props`](../Directory.Packages.props) — add a version there, reference without a version in the `.csproj`.
- The solution file is the XML `.slnx` format ([`Hex.Scaffold.slnx`](../Hex.Scaffold.slnx)).

## Containerised build

The [`Dockerfile`](../Dockerfile) is a 4-stage restore/build/publish/runtime pipeline targeting `mcr.microsoft.com/dotnet/aspnet:10.0`. It exposes port `8080`.

```bash
docker build -t hex-scaffold:local .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__PostgreSql='Host=host.docker.internal;Port=5432;Username=postgres;Password=postgres;Database=hex-scaffold' \
  hex-scaffold:local
```

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| App fails at startup with Npgsql error | PostgreSQL not running, or connection string wrong. Check `/ready`. |
| Readiness returns `Degraded` | Kafka unreachable — soft dep, app will still serve HTTP. |
| `404` on `/samples/{id}` after POST | Cache wasn't invalidated — indicates a bug in `SampleEventPublishHandler`, or the event was swallowed by Kafka publish failure. Check logs for `Failed to publish`. |
| Integration tests hang | Docker not running. |
| Architecture tests failing | A new `using` crossed a boundary. Check the failing type name in the assertion message. |
| EF migrations not applied in Dev | `Database:ApplyMigrationsOnStartup` must be `true` (default in `appsettings.Development.json`). |
| `ValueObjectValidationException` at boundary | Input failed a Vogen validator. Make sure validation runs before `SampleId.From(...)` / `SampleName.From(...)`. |

## CLAUDE.md

If you use Claude Code, the repo has a [`CLAUDE.md`](../CLAUDE.md) at the root with agent guidelines. Keep it up to date when you change architecture rules or package set.
