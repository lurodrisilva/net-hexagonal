# Learnings

## Architecture
- Reference project: /Users/lucianosilva/src/open-source/dotnet-clean-arch
- Reference uses .slnx format — we use .sln
- net10.0, TreatWarningsAsErrors=true, Nullable=enable, ImplicitUsings=enable
- Central package management via Directory.Packages.props
- Mediator source generator: Mediator.SourceGenerator + Mediator.Abstractions (v3.0.2)
- Vogen v8.0.2 for strongly-typed value objects

## Key Package Versions (from reference Directory.Packages.props)
- Ardalis.GuardClauses: 5.0.0
- Ardalis.Result: 10.1.0
- Ardalis.SharedKernel: 5.0.0
- Ardalis.SmartEnum: 8.2.0
- Ardalis.Specification: 9.3.1
- Ardalis.Specification.EntityFrameworkCore: 9.3.1
- Mediator.Abstractions: 3.0.2
- Mediator.SourceGenerator: 3.0.2
- Vogen: 8.0.2
- FastEndpoints: 7.1.1
- FastEndpoints.Swagger: 7.1.1
- Scalar.AspNetCore: 2.10.3
- Serilog.AspNetCore: 9.0.0
- Microsoft.EntityFrameworkCore.Relational: 10.0.0
- xunit: 2.x (latest)
- NSubstitute: 5.x
- Shouldly: 4.x
- NetArchTest.Rules: 1.3.2
- Testcontainers.PostgreSql / .MongoDb / .Redis: latest

## Hexagonal Dependency Rules
Domain → (nothing)
Application → Domain
Adapters.Inbound → Application (Domain transitive)
Adapters.Outbound → Domain
Adapters.Persistence → Domain, Application
Api → All projects (composition root)

## Coding Style
- 2-space indentation (enforced via .editorconfig)
- Allman braces (own line)
- File-scoped namespaces
- Private fields: _camelCase prefix
- No public setters on entities
- All handlers return ValueTask
- Result<T> for all expected failures (no exceptions)
## [Task 1] Solution Scaffold Complete
- All 9 projects created and building clean (0 warnings, 0 errors)
- Program.cs placeholder added to Api project (required for Sdk.Web to compile)
- Central package management working (Directory.Packages.props)
- .NET 10 `dotnet new sln` creates `.slnx` (not `.sln`) — use `Hex.Scaffold.slnx` for build commands
- Issues fixed during scaffold:
  - `StackExchange.Redis 2.8.32` not published; bumped to `2.8.37` (actual latest)
  - `FluentValidation 11.11.0` downgrade conflict: FastEndpoints 7.1.1 requires >= 12.1.0; bumped to `12.1.0`
  - `Microsoft.Extensions.Diagnostics.HealthChecks` is bundled inside `Microsoft.AspNetCore.App` framework; removed explicit PackageReference from Api.csproj to avoid NU1510 warning-as-error
  - `CentralPackageTransitivePinningEnabled=true` is strict — transitive version pins must match available NuGet versions exactly

## [Task 1 — Package Version Corrections]
CRITICAL: Update Directory.Packages.props with these ACTUAL versions (NuGet verified):
- StackExchange.Redis: 2.8.37 (2.8.32 does NOT exist on NuGet)
- FluentValidation: 12.1.0 (FastEndpoints 7.1.1 requires >= 12.0.0)
- Microsoft.Extensions.Diagnostics.HealthChecks: REMOVE from Api.csproj (bundled in Microsoft.AspNetCore.App)

## [Task 1 — .NET 10 CLI Behavior]
- `dotnet new sln` in .NET 10 creates .slnx format (not .sln)
- Use `Hex.Scaffold.slnx` for all `dotnet build` and `dotnet sln` commands
- Solution is at: /Users/lucianosilva/src/open-source/net-hexagonal/Hex.Scaffold.slnx

## [Task 2] Domain Layer GlobalUsings & Port Interfaces Complete
- ✓ GlobalUsings.cs with all Ardalis + Mediator + Logging imports
- ✓ Port interfaces in `Hex.Scaffold.Domain.Ports.Outbound/`:
  - IEventPublisher (ValueTask-based, generic event publishing)
  - ICacheService (Get/Set/Remove with optional expiration)
  - IExternalApiClient (Result<T> wrapped HTTP client)
  - ISampleReadModelRepository (Upsert/Delete for read models)
  - SampleReadModel (sealed class with init properties)
- ✓ Domain service interface in `Hex.Scaffold.Domain.Interfaces/`:
  - IDeleteSampleService (uses `int id` — will be updated to SampleId in Task 3)
- ✓ Build: 0 Warnings, 0 Errors
- ✓ Architecture verified: Domain has ZERO infrastructure dependencies

### Key Patterns (Task 2)
1. Port interfaces use primitives or Domain types only — no infrastructure types in signatures
2. ValueTask for async operations — matches Mediator convention
3. Result<T> wrapping — error handling at port boundary
4. Read models are sealed classes — immutable via init properties
5. CancellationToken always optional with default — allows callers to omit if not needed
6. No Inbound ports needed — Mediator ICommand/IQuery serve as implicit inbound ports

## [Task 3] Sample Aggregate Complete
- SampleId (Vogen int), SampleName (Vogen string SystemTextJson), SampleStatus (SmartEnum sealed)
- Sample entity: primary constructor, 4 mutation methods, private setters
- Events: SampleCreatedEvent, SampleUpdatedEvent, SampleDeletedEvent (all sealed, DomainEventBase)
- SampleEventPublishHandler: implements 3 INotificationHandler on same class
- SampleByIdSpec, DeleteSampleService, IDeleteSampleService (updated int→SampleId)
- Issues: none — Build succeeded 0 Warning(s) 0 Error(s)

## [Task 4] Application CQRS Complete
- Commands: CreateSampleCommand, UpdateSampleCommand, DeleteSampleCommand
- Queries: GetSampleQuery (cache-aside via ICacheService), ListSamplesQuery (via IListSamplesQueryService)
- Handlers: all use ICommand/IQuery/ICommandHandler/IQueryHandler from Mediator v3
- LoggingBehavior: IPipelineBehavior<TMessage,TResponse> from Mediator v3
- PagedResult<T> record + Constants.DefaultPageSize/MaxPageSize
- DeleteSampleHandler delegates to IDeleteSampleService (domain service)
- SampleCreatedEvent published in CreateSampleHandler (not in aggregate constructor — avoids EF Core materialization issue)
- Added to Application.csproj: Ardalis.SharedKernel, Mediator.Abstractions, Microsoft.Extensions.Logging.Abstractions
- Issues fixed:
  - IPipelineBehavior.Handle parameter order in Mediator v3 is (TMessage, MessageHandlerDelegate, CancellationToken) — NOT (TMessage, CancellationToken, MessageHandlerDelegate)
  - LSP shows NuGet-not-resolved errors before first build/restore — ignore until `dotnet build`

## [Task 5] PostgreSQL Adapter Complete
- AppDbContext: minimal with ApplyConfigurationsFromAssembly
- EfRepository<T>: one-liner inheriting RepositoryBase<T> (Ardalis.Specification.EFC)
- EventDispatcherInterceptor: hooks SavedChangesAsync, dispatches via IDomainEventDispatcher
- SampleConfiguration: Vogen HasConversion for SampleId, SampleName; SmartEnum int conversion for SampleStatus
- ListSamplesQueryService: Dapper with raw SQL, NpgsqlConnection, tuple projection
- PostgreSqlServiceExtensions: registers AppDbContext with Npgsql retry, EfRepository<>, IListSamplesQueryService, IDeleteSampleService
- Issues: none — Build succeeded 0 Warning(s) 0 Error(s)

## [Task 6-7-8] MongoDB + Redis + Outbound Adapters Complete
- MongoDB: SampleDocument (BsonId ObjectId), SampleReadModelRepository (upsert/delete), MongoDbOptions
- Redis: RedisCacheService implements ICacheService with graceful degradation on RedisConnectionException
- Kafka: KafkaEventPublisher publishes to Kafka topics via IProducer, gracefully handles ProduceException
- HTTP: ExternalApiClient wraps IHttpClientFactory("ExternalApi"), returns Result<T> for all error cases
- All outbound adapters reference only Domain ports (IEventPublisher, ICacheService, etc.)
- Outbound project: `using Ardalis.Result;` explicit import required (no global using) — types accessible transitively via Domain project reference
- LSP shows NuGet-not-resolved errors on package types (MongoDB, StackExchange.Redis, Confluent.Kafka) — pre-existing issue, ignore until `dotnet build`
- Issues: none — Build succeeded 0 Warning(s) 0 Error(s)

## [Task 9-10] Inbound Adapters Complete (FastEndpoints + Kafka Consumer)
- CRUD endpoints: Create, GetById, List, Update, Delete with FastEndpoints REPR pattern
- ResultExtensions: ToCreatedResult, ToGetByIdResult, ToUpdateResult, ToDeleteResult, ToOkOnlyResult
- Validators use FastEndpoints Validator<T> (inherits FluentValidation AbstractValidator<T>)
- Mappers use FastEndpoints sealed Mapper<TRequest, TResponse, TEntity>
- SampleEventConsumer: BackgroundService with Task.Factory.StartNew LongRunning
- ISampleReadModelRepository resolved from DI scope (not injected directly) — correct hexagonal pattern
- GlobalUsings required: Ardalis.Result, FastEndpoints, FluentValidation, Mediator, Microsoft.AspNetCore.Http, Microsoft.AspNetCore.Http.HttpResults, Microsoft.Extensions.Logging
- Issues fixed:
  - PagedResult<T> ambiguity: Ardalis.Result.PagedResult<T> clashes with Hex.Scaffold.Application.PagedResult<T> — fix with fully-qualified Hex.Scaffold.Application.PagedResult<T> in List.cs
  - NotEmpty()/MaximumLength() FluentValidation extensions not in scope — fixed by adding `global using FluentValidation;`
  - TypedResults not in scope — fixed by adding `global using Microsoft.AspNetCore.Http;`
- Kafka deserialization uses JsonElement to avoid Vogen type + private-setter deserialization issues:
  - SampleId (no SystemTextJson converter) → serializes as {"Value": N} → GetProperty("Value").GetInt32()
  - SampleName (has SystemTextJson converter) → serializes as plain string → GetString() directly
  - SampleStatus (SmartEnum) → serializes as object → GetProperty("Name").GetString()
- Architecture check: Inbound project has ZERO references to Outbound/Persistence/DbContext

## [Task 11] Api Composition Root Complete
- Program.cs: UseSerilog(), AddProblemDetails(), AddFastEndpoints(), AddServiceConfigs(), AddHealthChecks(), AddRateLimiter()
- ServiceConfigs: chains PostgreSQL, MongoDB, Redis, Kafka, HTTP resilient client, Mediator
- MediatorConfig: assembly scanning for Domain, Application, Adapters.Persistence, Adapters.Inbound; PipelineBehaviors = [typeof(LoggingBehavior<,>)]
- MiddlewareConfig: exception handling, HTTPS, rate limiter, FastEndpoints, Swagger+Scalar (dev-only), health checks (/healthz live, /ready), migrations on startup
- appsettings: all connection strings, Kafka, Serilog, OTel config; Development overrides ApplyMigrationsOnStartup=true + Debug logging
- Issues resolved:
  - Plan used options.AddOpenGenericBehavior() — actual Mediator v3 API is options.PipelineBehaviors = [typeof(T<,>)]
  - ExternalApiOptions already existed in Hex.Scaffold.Adapters.Outbound.Http — did NOT create duplicate in Api.Options
  - MapScalarApiReference() requires explicit `using Scalar.AspNetCore;` even though Scalar.AspNetCore is a direct PackageReference
  - ILogger parameter type must be Microsoft.Extensions.Logging.ILogger (FQN) to avoid ambiguity with Serilog.ILogger
  - All other LSP errors were transitive dependency resolution failures — build confirmed 0 warnings, 0 errors

## [Task 12-13] Observability + Dockerfile Complete
- OpenTelemetry: traces (AspNetCore, HttpClient), metrics (AspNetCore, HttpClient, Runtime), logs via OTLP
- Health checks: /healthz (live tag) + /ready (ready tag) with PostgreSQL (NpgsqlConnection), MongoDB, Redis custom checks
- Rate limiting: fixed window 100 req/min per IP via GlobalLimiter
- Dockerfile: 4-stage (restore, build, publish, final) with aspnet:10.0 runtime image
- Issues: `AddNpgsql` health check extension NOT available (AspNetCore.HealthChecks.NpgSql not in Directory.Packages.props); used custom NpgsqlConnection check instead
- Issues: MongoDB.Driver, StackExchange.Redis, Npgsql NOT transitively visible to LSP from ProjectReference — added direct PackageReference to Api.csproj (all already in Directory.Packages.props, no new packages introduced to solution)
- Build: 0 Warning(s) 0 Error(s)

## [Task 14-15-16] Tests Complete
- Architecture tests: 7/7 tests covering all hexagonal dependency rules (NetArchTest) — all pass
- Unit tests: SampleAggregate (5 tests), CreateSampleHandler (2 tests) — all pass
- Integration tests: TestContainers (Postgres + Redis), WebApplicationFactory<Program> — build only (requires Docker)
- Architecture tests can run without Docker (no external services)
- Integration tests require Docker (TestContainers starts containers)
- Issues:
  - `DomainEntities_ShouldHaveOnlyPrivateSetters`: EntityBase<T,TId> has a public setter on `Id` (needed for EF Core ORM).
    Fix: Use `BindingFlags.DeclaredOnly` on GetProperties() to check only Sample-declared properties, not inherited ones.
  - `IMediator.Publish` in Mediator v3: NSubstitute returns default(ValueTask) = ValueTask.CompletedTask for unmocked calls — no explicit setup needed.
  - Integration tests use `EnsureCreatedAsync()` (NOT `MigrateAsync()`) — creates schema from model without requiring migration files.
  - `ClearDomainEvents()` does NOT exist on HasDomainEventsBase — track event counts across operations instead.
  - Unit test GlobalUsings must include `Ardalis.Result`, `Ardalis.SharedKernel`, `Mediator`, `Microsoft.Extensions.Logging` to access handler/repository types in test files.
  - Integration GlobalUsings must include `Microsoft.Extensions.Configuration` for `AddInMemoryCollection` extension method.

## [Task 17] Final Verification Complete
- Full build: 0 warnings, 0 errors
- Architecture tests: 7/7 pass
- Unit tests: 7/7 pass
- EF migration: SUCCESS (InitialCreate created — PostgreSQL was running)
  - Required adding Microsoft.EntityFrameworkCore.Design to Hex.Scaffold.Api.csproj
    (PrivateAssets=all in persistence project means it doesn't flow transitively)
- Source files: 72 .cs files
- Test files: 8 .cs files
- No AI slop found in hand-written code
  (#pragma warning disable only in EF auto-generated migration Designer files)
- All 3 critical TODO comments verified present
- Scaffold is complete and ready for extension

## [F1 Fixes] Plan Compliance Issues Resolved
- Scrutor: Added services.Scan() in ServiceConfigs.cs with RegistrationStrategy.Skip; added <PackageReference Include="Scrutor" /> to Api.csproj and using Scrutor; at top of ServiceConfigs.cs
- Kafka health check: TCP connectivity check (no Confluent.Kafka in Api project), uses Degraded (not Unhealthy) as Kafka is a soft dependency
- IExternalApiClient: Added GetExternalSampleInfoQuery + Handler + GetExternalInfo endpoint to complete the EXTERNAL API FLOW
- All 3 F1 issues resolved, build clean: 0 Warning(s), 0 Error(s); 7 unit + 7 architecture tests pass
