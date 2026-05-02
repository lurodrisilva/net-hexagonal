<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Adapters.Persistence

## Purpose
Driven adapters for persistence: EF Core over PostgreSQL (the primary store), MongoDB (alternate), and Redis (optional read-through cache). Implements `IRepository<T>`, `IReadRepository<T>`, and `ICacheService` from Domain. Persistence is also the only adapter project allowed to reference `Hex.Scaffold.Application` — query services live here next to their EF queries.

## Key Files
| File | Description |
|------|-------------|
| `GlobalUsings.cs` | Imports for EF Core, Npgsql, etc. |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Common/` | `EfRepository<T>` (generic specification-based repository), `EventDispatcherInterceptor`, `MediatorDomainEventDispatcher` |
| `Extensions/` | `PostgreSqlServiceExtensions`, `MongoServiceExtensions`, `RedisServiceExtensions` — DI helpers consumed by the Api composition root |
| `PostgreSql/` | `AppDbContext`, `DesignTimeDbContextFactory`, `Config/` (entity configs incl. `AccountConfiguration` for jsonb columns), `Migrations/`, `Queries/` (query-service implementations like `ListAccountsQueryService`) |
| `MongoDb/` | Mongo-backed repository implementations (alternate persistence) |
| `Redis/` | `RedisCacheService` implementing `ICacheService` |

## For AI Agents

### Working In This Directory
- The `NpgsqlDataSource` is built once at startup with `EnableDynamicJson()` (`PostgreSqlServiceExtensions.cs`). Without that flag Npgsql rejects parameter binding to `jsonb` columns. Don't construct `NpgsqlConnection` directly anywhere — borrow from the singleton data source.
- jsonb columns (`configuration`, `identity`, `defaults`, `requirements`, `future_requirements`, `metadata`) are stored as raw JSON strings on the aggregate. The mapping is configured in `PostgreSql/Config/AccountConfiguration.cs`. Adding a new nested object means: add property on aggregate, add `HasColumnType("jsonb")` in the config, add an EF migration.
- `EventDispatcherInterceptor` runs after `SaveChangesAsync` and dispatches each entity's `DomainEvents` through `MediatorDomainEventDispatcher`. Don't dispatch events manually elsewhere.
- EF migrations: `dotnet ef migrations add <Name> --project src/Hex.Scaffold.Adapters.Persistence --startup-project src/Hex.Scaffold.Api`. The `DesignTimeDbContextFactory` reads `ConnectionStrings__PostgreSql` from environment.
- Query services (e.g. `ListAccountsQueryService`) implement Application-defined ports and return `PagedResult<TDto>` projections. Use compiled queries or `AsNoTracking` for hot reads.

### Testing Requirements
- Integration tests under `tests/Hex.Scaffold.Tests.Integration` use Testcontainers for Postgres and Redis. Docker must be running.

### Common Patterns
- One `IEntityTypeConfiguration<T>` per aggregate, in `PostgreSql/Config/`.
- Generic `EfRepository<T>` uses `Specification<T>` for composable predicates — avoid bespoke repository classes.

## Dependencies

### Internal
- `Hex.Scaffold.Domain` (entities, ports)
- `Hex.Scaffold.Application` (query-service ports — exception to the "adapters don't reference each other" rule, allowed only for query services)

### External
- `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`
- `MongoDB.Driver`
- `StackExchange.Redis`

<!-- MANUAL: -->
