# Hex.Scaffold — .NET 10 Hexagonal Architecture Project Scaffold

## TL;DR

> **Quick Summary**: Build a complete .NET 10 hexagonal architecture scaffold ("Hex.Scaffold") with 7 projects, implementing all ports and adapters (REST API, Kafka, PostgreSQL, MongoDB, Redis, HTTP Client) based on the Ardalis Clean Architecture reference, with a Sample aggregate demonstrating end-to-end flow.
>
> **Deliverables**:
> - Solution with 7 source projects + 3 test projects following hexagonal Ports & Adapters
> - Sample aggregate demonstrating CRUD (PostgreSQL), caching (Redis), event streaming (Kafka), read model (MongoDB), external API calls (HTTP Resilience)
> - OpenTelemetry, Health Checks, Rate Limiting, OpenAPI, ProblemDetails configured
> - Multi-stage Dockerfile for AKS deployment
> - Architecture tests enforcing hexagonal dependency rules via NetArchTest
>
> **Estimated Effort**: Large (17 tasks, ~80-100 files)
> **Parallel Execution**: YES — 4 waves
> **Critical Path**: Task 1 → Task 2 → Task 3/4 → Tasks 5-9 → Tasks 10-11 → Task 12-13 → Task 14-17

---

## Context

### Original Request
Build a .NET 10 scaffold project with hexagonal architecture based on the `dotnet-clean-arch` skill and the reference project at `/Users/lucianosilva/src/open-source/dotnet-clean-arch`. The architecture must adhere to a specific Mermaid diagram showing: REST API + Kafka Consumer (inbound), Application + Domain (core), REST Client + Kafka Producer (outbound), and PostgreSQL + MongoDB + Redis (persistence). Deploy target is Azure AKS.

### Interview Summary
**Key Discussions**:
- **Solution name**: `Hex.Scaffold` — all namespaces follow `Hex.Scaffold.*`
- **Example entity**: YES — full end-to-end Sample aggregate touching all adapters
- **SDKs**: All mandatory + all optional EXCEPT Quartz.NET
- **DevOps**: Dockerfile only (multi-stage), no K8s manifests
- **Tests**: Unit + Integration + Architecture (xUnit, TestContainers, NetArchTest)
- **Hellang.Middleware.ProblemDetails**: REPLACED with built-in `AddProblemDetails()` (Metis recommendation — .NET 10 has native support, Hellang may conflict with FastEndpoints)

**Research Findings**:
- Reference project: net10.0, Directory.Build.props/Packages.props, Mediator source-gen, EF Core SaveChangesInterceptor, FastEndpoints REPR, Serilog, OpenTelemetry via Aspire ServiceDefaults
- SDK patterns: Kafka producer wrapper + consumer BackgroundService, OpenTelemetry OTLP extension, MongoDB generic repo, Redis ICacheService, HTTP resilience standard handler, EF Core Npgsql with retry
- Architecture mapping: Clean Arch 4-layer → Hexagonal 7-project structure

### Metis Review
**Identified Gaps** (all addressed):
- Data flow for example entity: DEFINED (see Example Entity Data Flow below)
- MongoDB role: DEFINED as denormalized read/audit store via Kafka consumer (CQRS read model)
- Redis strategy: Cache-aside with TTL on reads, invalidation on writes via domain events
- Startup resilience: PostgreSQL required (fail fast), Kafka/MongoDB/Redis soft deps (degraded mode)
- Hellang ProblemDetails: DROPPED in favor of built-in .NET 10 ProblemDetails
- Vogen serialization for MongoDB/Redis: ADDRESSED as explicit tasks
- Dual-write consistency: Accepted eventual consistency, TODO comment for Outbox Pattern

### Example Entity Data Flow (CRITICAL — governs all adapter interactions)

```
CREATE FLOW:
  REST POST /samples → FastEndpoint → CreateSampleCommand → Handler
    → Save to PostgreSQL (EF Core IRepository)
    → Domain Event: SampleCreatedEvent
    → Event Handler: Publish to Kafka topic "sample-events"
    → Event Handler: Invalidate Redis cache for sample list

READ FLOW (single):
  REST GET /samples/{id} → FastEndpoint → GetSampleQuery → Handler
    → Check Redis cache (ICacheService.GetAsync)
    → Cache miss → Query PostgreSQL (EF Core Specification)
    → Write to Redis cache (ICacheService.SetAsync with TTL)
    → Return DTO

READ FLOW (list):
  REST GET /samples?page=1&perPage=10 → FastEndpoint → ListSamplesQuery → Handler
    → Query PostgreSQL via Dapper (IListSamplesQueryService)
    → Return paginated DTOs

UPDATE FLOW:
  REST PUT /samples/{id} → FastEndpoint → UpdateSampleCommand → Handler
    → Load from PostgreSQL → Mutate aggregate → Save
    → Domain Event: SampleUpdatedEvent
    → Event Handler: Publish to Kafka + Invalidate Redis cache

DELETE FLOW:
  REST DELETE /samples/{id} → FastEndpoint → DeleteSampleCommand → Handler
    → Domain Service (IDeleteSampleService) → Delete + Publish event
    → Domain Event: SampleDeletedEvent
    → Event Handler: Publish to Kafka + Remove from Redis cache

KAFKA CONSUMER FLOW (async, writes read model):
  Kafka topic "sample-events" → KafkaConsumerHostedService
    → Deserialize event → Write/Update/Delete document in MongoDB
    → This creates a denormalized CQRS read model for cross-service queries

EXTERNAL API FLOW (example outbound):
  Application use case → IExternalApiClient (port in Domain)
    → Resilient HTTP Client (implementation in Adapters.Outbound)
    → Calls external service with retry, circuit breaker, timeout
```

---

## Work Objectives

### Core Objective
Create a production-ready .NET 10 hexagonal architecture scaffold that serves as a canonical template for building microservices with Kafka, PostgreSQL, MongoDB, and Redis adapters, following Ports & Adapters principles with strict dependency inversion.

### Concrete Deliverables
- `Hex.Scaffold.sln` with 10 projects (7 source + 3 test)
- `Hex.Scaffold.Domain` — Entities, Value Objects, Domain Events, Port interfaces
- `Hex.Scaffold.Application` — CQRS Commands/Queries, DTOs, Application services
- `Hex.Scaffold.Adapters.Inbound` — FastEndpoints REST API, Kafka Consumer
- `Hex.Scaffold.Adapters.Outbound` — Kafka Producer, Resilient HTTP Client
- `Hex.Scaffold.Adapters.Persistence` — EF Core/PostgreSQL, MongoDB, Redis repositories
- `Hex.Scaffold.Api` — Composition root (Program.cs, DI, middleware, configs)
- `Hex.Scaffold.Tests.Unit` — Domain + Application unit tests
- `Hex.Scaffold.Tests.Integration` — TestContainers integration tests
- `Hex.Scaffold.Tests.Architecture` — NetArchTest hexagonal dependency rules
- `Dockerfile` — Multi-stage build for AKS deployment

### Definition of Done
- [ ] `dotnet build Hex.Scaffold.sln` exits 0 with zero warnings
- [ ] `dotnet test --filter "Category=Unit"` exits 0 — all pass
- [ ] `dotnet test --filter "Category=Architecture"` exits 0 — all dependency rules verified
- [ ] `docker build -t hex-scaffold .` exits 0, image < 200MB
- [ ] Sample CRUD endpoints return correct HTTP status codes
- [ ] Health checks report per-adapter status

### Must Have
- Hexagonal dependency rules enforced via NetArchTest
- All 6 adapters implemented and wired (REST API, Kafka Consumer, Kafka Producer, HTTP Client, PostgreSQL, MongoDB, Redis)
- Sample aggregate demonstrating all data flows
- OpenTelemetry with OTLP exporter (traces, metrics, logs)
- Health checks: `/healthz` (liveness) + `/ready` (readiness per adapter)
- Rate limiting configured on API endpoints
- OpenAPI documentation accessible at `/swagger`
- ProblemDetails for standardized error responses (built-in .NET, NOT Hellang)
- FluentValidation on endpoint requests
- Dapper for list query service (demonstrating EF Core + Dapper coexistence)
- Scrutor for adapter assembly scanning
- 2-space indentation, Allman braces, file-scoped namespaces (per skill coding style)

### Must NOT Have (Guardrails)
- **NO Quartz.NET** — separate project
- **NO Aspire** (no AppHost, no ServiceDefaults dependency) — scaffold is standalone
- **NO Kubernetes manifests / Helm charts / CI-CD pipelines**
- **NO authentication beyond `AllowAnonymous()`** — placeholder TODO comment only
- **NO Transactional Outbox** — accept eventual consistency, add TODO comment
- **NO multiple example entities** — one Sample aggregate only
- **NO Hellang.Middleware.ProblemDetails** — use built-in `AddProblemDetails()`
- **NO Schema Registry / Avro / Protobuf for Kafka** — simple JSON serialization
- **NO Redis Pub/Sub / Streams / Lua scripts** — simple get/set with TTL
- **NO MongoDB indexes / sharding / change streams** — simple document CRUD
- **NO custom OpenTelemetry spans/metrics** — auto-instrumentation + OTLP only
- **NO sliding window / token bucket rate limiting** — fixed-window in-memory only
- Domain project MUST NEVER reference Application, any Adapter, or Api
- Application MUST NEVER reference any Adapter or Api
- Adapters.Inbound MUST NEVER reference Adapters.Outbound or Adapters.Persistence
- Adapters.Outbound MUST NEVER reference Application, Adapters.Inbound, or Adapters.Persistence
- **NO AI slop**: No excessive comments, no over-abstraction, no generic names (data/result/item/temp), no empty catches, no `as any` / `#pragma warning disable`

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: NO (new project)
- **Automated tests**: YES (tests-after — test projects with examples)
- **Framework**: xUnit + NSubstitute + Shouldly + TestContainers + NetArchTest
- **Build gate**: `dotnet build Hex.Scaffold.sln` must pass with zero warnings after EVERY task

### QA Policy
Every task MUST include agent-executed QA scenarios.
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **Build verification**: `dotnet build Hex.Scaffold.sln` after every task
- **Test verification**: `dotnet test` for test-related tasks
- **Docker verification**: `docker build` for Dockerfile task
- **LSP diagnostics**: Check changed files for errors before build

### Skill Requirement
- **ALL implementation tasks MUST load skill `dotnet-clean-arch`** — it defines coding style, patterns, base classes, anti-patterns, and the verification checklist (Phase 5 of the skill)

---

## Execution Strategy

### Project Structure (Hexagonal Mapping)

```
Hex.Scaffold/
├── Hex.Scaffold.sln
├── Directory.Build.props            ← net10.0, TreatWarningsAsErrors, nullable, implicit usings
├── Directory.Packages.props         ← Central package version management
├── global.json                      ← SDK version pinning
├── .editorconfig                    ← 2-space indent, Allman braces, file-scoped namespaces
├── Dockerfile                       ← Multi-stage build
│
├── src/
│   ├── Hex.Scaffold.Domain/                          ← CORE: Entities, VOs, Events, Port Interfaces
│   │   ├── SampleAggregate/
│   │   │   ├── Sample.cs                             ← Aggregate Root
│   │   │   ├── SampleId.cs                           ← Vogen strongly-typed ID
│   │   │   ├── SampleName.cs                         ← Vogen value object
│   │   │   ├── SampleStatus.cs                       ← SmartEnum
│   │   │   ├── Events/
│   │   │   │   ├── SampleCreatedEvent.cs
│   │   │   │   ├── SampleUpdatedEvent.cs
│   │   │   │   └── SampleDeletedEvent.cs
│   │   │   ├── Handlers/
│   │   │   │   └── SampleEventPublishHandler.cs      ← Publishes to IEventPublisher port
│   │   │   └── Specifications/
│   │   │       └── SampleByIdSpec.cs
│   │   ├── Ports/
│   │   │   ├── Inbound/                              ← (empty — inbound ports are Mediator commands/queries)
│   │   │   └── Outbound/
│   │   │       ├── IEventPublisher.cs                ← Kafka producer port
│   │   │       ├── IExternalApiClient.cs             ← HTTP client port
│   │   │       ├── ICacheService.cs                  ← Redis cache port
│   │   │       └── ISampleReadModelRepository.cs     ← MongoDB read model port
│   │   ├── Services/
│   │   │   └── DeleteSampleService.cs                ← Domain service
│   │   ├── Interfaces/
│   │   │   └── IDeleteSampleService.cs
│   │   └── GlobalUsings.cs
│   │
│   ├── Hex.Scaffold.Application/                     ← CORE: CQRS Use Cases
│   │   ├── Samples/
│   │   │   ├── SampleDto.cs
│   │   │   ├── Create/
│   │   │   │   ├── CreateSampleCommand.cs
│   │   │   │   └── CreateSampleHandler.cs
│   │   │   ├── Get/
│   │   │   │   ├── GetSampleQuery.cs
│   │   │   │   └── GetSampleHandler.cs
│   │   │   ├── List/
│   │   │   │   ├── ListSamplesQuery.cs
│   │   │   │   ├── ListSamplesHandler.cs
│   │   │   │   └── IListSamplesQueryService.cs
│   │   │   ├── Update/
│   │   │   │   ├── UpdateSampleCommand.cs
│   │   │   │   └── UpdateSampleHandler.cs
│   │   │   └── Delete/
│   │   │       ├── DeleteSampleCommand.cs
│   │   │       └── DeleteSampleHandler.cs
│   │   ├── Behaviors/
│   │   │   └── LoggingBehavior.cs
│   │   ├── Constants.cs
│   │   ├── PagedResult.cs
│   │   └── GlobalUsings.cs
│   │
│   ├── Hex.Scaffold.Adapters.Inbound/                ← ADAPTER: REST API + Kafka Consumer
│   │   ├── Api/
│   │   │   ├── Samples/
│   │   │   │   ├── SampleRecord.cs                   ← API response record
│   │   │   │   ├── Create.cs                         ← POST endpoint
│   │   │   │   ├── Create.CreateSampleRequest.cs
│   │   │   │   ├── Create.CreateSampleValidator.cs
│   │   │   │   ├── Create.CreateSampleResponse.cs
│   │   │   │   ├── GetById.cs                        ← GET endpoint
│   │   │   │   ├── GetById.GetSampleByIdRequest.cs
│   │   │   │   ├── GetById.GetSampleByIdMapper.cs
│   │   │   │   ├── List.cs                           ← GET (collection)
│   │   │   │   ├── List.ListSamplesRequest.cs
│   │   │   │   ├── List.ListSamplesMapper.cs
│   │   │   │   ├── Update.cs                         ← PUT endpoint
│   │   │   │   ├── Update.UpdateSampleRequest.cs
│   │   │   │   ├── Update.UpdateSampleValidator.cs
│   │   │   │   ├── Update.UpdateSampleMapper.cs
│   │   │   │   ├── Delete.cs                         ← DELETE endpoint
│   │   │   │   └── Delete.DeleteSampleRequest.cs
│   │   │   └── Extensions/
│   │   │       └── ResultExtensions.cs               ← Result<T> → TypedResults mapping
│   │   ├── Messaging/
│   │   │   └── SampleEventConsumer.cs                ← Kafka consumer BackgroundService
│   │   └── GlobalUsings.cs
│   │
│   ├── Hex.Scaffold.Adapters.Outbound/               ← ADAPTER: Kafka Producer + HTTP Client
│   │   ├── Messaging/
│   │   │   └── KafkaEventPublisher.cs                ← IEventPublisher implementation
│   │   ├── Http/
│   │   │   └── ExternalApiClient.cs                  ← IExternalApiClient implementation
│   │   └── GlobalUsings.cs
│   │
│   ├── Hex.Scaffold.Adapters.Persistence/            ← ADAPTER: PostgreSQL + MongoDB + Redis
│   │   ├── PostgreSql/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── EfRepository.cs
│   │   │   ├── EventDispatcherInterceptor.cs
│   │   │   ├── Config/
│   │   │   │   └── SampleConfiguration.cs
│   │   │   ├── Queries/
│   │   │   │   └── ListSamplesQueryService.cs        ← Dapper implementation
│   │   │   └── Migrations/
│   │   ├── MongoDb/
│   │   │   ├── SampleReadModelRepository.cs          ← ISampleReadModelRepository impl
│   │   │   ├── SampleDocument.cs                     ← MongoDB document model
│   │   │   └── Serializers/
│   │   │       └── VogenBsonSerializers.cs           ← Vogen type BSON serializers
│   │   ├── Redis/
│   │   │   └── RedisCacheService.cs                  ← ICacheService implementation
│   │   ├── Extensions/
│   │   │   ├── PostgreSqlServiceExtensions.cs
│   │   │   ├── MongoDbServiceExtensions.cs
│   │   │   └── RedisServiceExtensions.cs
│   │   └── GlobalUsings.cs
│   │
│   └── Hex.Scaffold.Api/                             ← COMPOSITION ROOT
│       ├── Program.cs
│       ├── Configurations/
│       │   ├── ServiceConfigs.cs
│       │   ├── MediatorConfig.cs
│       │   ├── MiddlewareConfig.cs
│       │   ├── ObservabilityConfig.cs                ← OpenTelemetry + Serilog
│       │   ├── HealthCheckConfig.cs
│       │   └── RateLimitingConfig.cs
│       ├── Options/
│       │   ├── KafkaOptions.cs
│       │   ├── MongoDbOptions.cs
│       │   ├── RedisOptions.cs
│       │   └── ExternalApiOptions.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── appsettings.Testing.json
│       └── GlobalUsings.cs
│
└── tests/
    ├── Hex.Scaffold.Tests.Unit/
    │   ├── Domain/
    │   │   └── SampleAggregateTests.cs
    │   └── Application/
    │       └── CreateSampleHandlerTests.cs
    │
    ├── Hex.Scaffold.Tests.Integration/
    │   ├── Fixtures/
    │   │   └── IntegrationTestFixture.cs             ← TestContainers setup
    │   └── Persistence/
    │       └── SampleRepositoryTests.cs
    │
    └── Hex.Scaffold.Tests.Architecture/
        └── HexagonalDependencyTests.cs               ← NetArchTest rules
```

### Hexagonal Dependency Rules (enforced by NetArchTest)

| Project | Can Reference | MUST NEVER Reference |
|---------|---------------|----------------------|
| **Domain** | Nothing (NuGet only: SharedKernel, Vogen, Mediator.Abstractions, GuardClauses, Specification, SmartEnum) | Application, any Adapter, Api |
| **Application** | Domain | Any Adapter, Api |
| **Adapters.Inbound** | Application, Domain (transitive) | Adapters.Outbound, Adapters.Persistence |
| **Adapters.Outbound** | Domain | Application, Adapters.Inbound, Adapters.Persistence |
| **Adapters.Persistence** | Domain, Application | Adapters.Inbound, Adapters.Outbound |
| **Api** | All projects (composition root) | — |

### Parallel Execution Waves

```
Wave 1 (Foundation — start immediately):
├── Task 1:  Solution scaffold (sln, props, global.json, editorconfig, all csproj) [quick]
├── Task 2:  Domain base types + port interfaces [quick]

Wave 2 (Core + Adapters — after Wave 1):
├── Task 3:  Domain Sample aggregate (entity, VOs, events, specs, services) [deep]
├── Task 4:  Application CQRS commands/queries/handlers + behaviors [deep]
├── Task 5:  Adapters.Persistence — PostgreSQL (EF Core, DbContext, config, interceptor, Dapper query) [unspecified-high]
├── Task 6:  Adapters.Persistence — MongoDB (client, repository, Vogen BSON serializers) [unspecified-high]
├── Task 7:  Adapters.Persistence — Redis (cache service, Vogen JSON serialization) [unspecified-high]
├── Task 8:  Adapters.Outbound — Kafka producer + HTTP resilient client [unspecified-high]

Wave 3 (Inbound + Composition — after Wave 2):
├── Task 9:  Adapters.Inbound — FastEndpoints CRUD + Result extensions [deep]
├── Task 10: Adapters.Inbound — Kafka consumer BackgroundService [unspecified-high]
├── Task 11: Api — Program.cs, DI wiring, middleware pipeline [deep]
├── Task 12: Api — Observability, Health checks, Rate limiting, OpenAPI, ProblemDetails [unspecified-high]
├── Task 13: Dockerfile multi-stage [quick]

Wave 4 (Tests + Verification — after Wave 3):
├── Task 14: Tests.Architecture — NetArchTest hexagonal dependency rules [unspecified-high]
├── Task 15: Tests.Unit — Domain + Application unit tests [unspecified-high]
├── Task 16: Tests.Integration — TestContainers integration tests [unspecified-high]
├── Task 17: Final build verification + appsettings review [quick]

Wave FINAL (4 parallel reviews, then user okay):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high)
└── Task F4: Scope fidelity check (deep)
-> Present results -> Get explicit user okay

Critical Path: Task 1 → Task 2 → Task 3 → Task 5 → Task 9 → Task 11 → Task 14 → F1-F4
Parallel Speedup: ~65% faster than sequential
Max Concurrent: 6 (Wave 2)
```

### Dependency Matrix

| Task | Depends On | Blocks | Wave |
|------|-----------|--------|------|
| **1** | — | 2-17 | 1 |
| **2** | 1 | 3-8 | 1 |
| **3** | 2 | 4, 5, 6, 7, 8, 9, 10 | 2 |
| **4** | 2, 3 | 9, 10, 11 | 2 |
| **5** | 2, 3 | 9, 11 | 2 |
| **6** | 2, 3 | 10, 11 | 2 |
| **7** | 2 | 11 | 2 |
| **8** | 2, 3 | 10, 11 | 2 |
| **9** | 4, 5 | 11 | 3 |
| **10** | 4, 6, 8 | 11 | 3 |
| **11** | 5, 6, 7, 8, 9, 10 | 12, 13 | 3 |
| **12** | 11 | 17 | 3 |
| **13** | 11 | 17 | 3 |
| **14** | 1 (csproj refs only) | 17 | 4 |
| **15** | 3, 4 | 17 | 4 |
| **16** | 5, 6, 7, 11 | 17 | 4 |
| **17** | 12, 13, 14, 15, 16 | F1-F4 | 4 |

### Agent Dispatch Summary

| Wave | Tasks | Dispatch |
|------|-------|----------|
| **1** | 2 | T1 → `quick`, T2 → `quick` |
| **2** | 6 | T3 → `deep`, T4 → `deep`, T5 → `unspecified-high`, T6 → `unspecified-high`, T7 → `unspecified-high`, T8 → `unspecified-high` |
| **3** | 5 | T9 → `deep`, T10 → `unspecified-high`, T11 → `deep`, T12 → `unspecified-high`, T13 → `quick` |
| **4** | 4 | T14 → `unspecified-high`, T15 → `unspecified-high`, T16 → `unspecified-high`, T17 → `quick` |
| **FINAL** | 4 | F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep` |

---

## TODOs

- [x] 1. Solution Scaffold — Project Structure & Build Configuration

  **What to do**:
  - Create `Hex.Scaffold.sln` with all 10 projects (7 source + 3 test)
  - Create `Directory.Build.props`: `net10.0`, `TreatWarningsAsErrors`, `nullable enable`, `implicit usings enable`, `latest` language version
  - Create `Directory.Packages.props`: central package version management for ALL NuGet packages listed in SDKs section
  - Create `global.json`: pin .NET 10 SDK version
  - Create `.editorconfig`: 2-space indentation, Allman braces, file-scoped namespaces, UTF-8 BOM (copy conventions from reference project at `/Users/lucianosilva/src/open-source/dotnet-clean-arch/.editorconfig`)
  - Create all 10 `.csproj` files with correct `<ProjectReference>` following the hexagonal dependency rules table
  - **Hex.Scaffold.Domain.csproj**: `Sdk="Microsoft.NET.Sdk"`, NO project references, NuGet: Ardalis.SharedKernel, Ardalis.GuardClauses, Ardalis.Result, Ardalis.SmartEnum, Ardalis.Specification, Mediator.Abstractions, Vogen, Microsoft.Extensions.Logging.Abstractions
  - **Hex.Scaffold.Application.csproj**: `Sdk="Microsoft.NET.Sdk"`, references Domain, NuGet: Ardalis.Result, Dapper
  - **Hex.Scaffold.Adapters.Inbound.csproj**: `Sdk="Microsoft.NET.Sdk"`, references Application, add `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, NuGet: FastEndpoints, FastEndpoints.Swagger, FluentValidation, Ardalis.Result.AspNetCore, Confluent.Kafka
  - **Hex.Scaffold.Adapters.Outbound.csproj**: `Sdk="Microsoft.NET.Sdk"`, references Domain, NuGet: Confluent.Kafka, Microsoft.Extensions.Http.Resilience, Microsoft.Extensions.Hosting.Abstractions
  - **Hex.Scaffold.Adapters.Persistence.csproj**: `Sdk="Microsoft.NET.Sdk"`, references Domain + Application, NuGet: Npgsql.EntityFrameworkCore.PostgreSQL, MongoDB.Driver, StackExchange.Redis, Ardalis.Specification.EntityFrameworkCore, Ardalis.SharedKernel, Vogen, Dapper, Scrutor, Microsoft.Extensions.Options.ConfigurationExtensions
  - **Hex.Scaffold.Api.csproj**: `Sdk="Microsoft.NET.Sdk.Web"`, references ALL 5 source projects, NuGet: Mediator.SourceGenerator, Serilog.AspNetCore, Scalar.AspNetCore, Microsoft.AspNetCore.OpenApi, OpenTelemetry.*, Azure.Identity, Microsoft.Extensions.Diagnostics.HealthChecks, Microsoft.AspNetCore.RateLimiting
  - **Test .csproj files**: reference appropriate source projects, NuGet: xunit, xunit.runner.visualstudio, NSubstitute, Shouldly, NetArchTest.Rules (arch), Testcontainers.* (integration), Microsoft.AspNetCore.Mvc.Testing (integration)
  - Create empty `GlobalUsings.cs` placeholder in each source project
  - Verify: `dotnet build Hex.Scaffold.sln` compiles with zero warnings and zero errors (empty projects compile)

  **Must NOT do**:
  - Do NOT add any business logic, entities, or implementations — this is structure only
  - Do NOT add Quartz.NET, Hellang, or Aspire packages
  - Do NOT deviate from the dependency rules table (e.g., Domain must have ZERO project references)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: File creation and configuration — no complex logic, many small files
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Provides coding style rules (.editorconfig, naming), project structure conventions, and NuGet package list from the reference project

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 1 (first task)
  - **Blocks**: Tasks 2-17
  - **Blocked By**: None (start immediately)

  **References**:

  **Pattern References** (existing code to follow):
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/Clean.Architecture.slnx` — Solution file format (use .sln, not .slnx if preferred)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/Directory.Build.props` — Build props pattern (net10.0, TreatWarningsAsErrors, nullable, implicit usings)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/Directory.Packages.props` — Central package management pattern with all version pins
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/.editorconfig` — Full .editorconfig to copy/adapt (2-space indent, Allman, file-scoped)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/Clean.Architecture.Core.csproj` — Core project structure (zero project refs, NuGet only)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Clean.Architecture.Web.csproj` — Web/Api project structure (references all layers)

  **WHY Each Reference Matters**:
  - Directory.Build.props: Exact build settings to replicate (the reference already uses net10.0 and TreatWarningsAsErrors)
  - Directory.Packages.props: Version pinning pattern — adapt package list to hexagonal SDKs
  - .editorconfig: MUST match coding style rules from `dotnet-clean-arch` skill (2-space, Allman, file-scoped)
  - Core .csproj: Shows how the innermost layer has zero project references — replicate for Domain
  - Web .csproj: Shows composition root referencing all layers — replicate for Api

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Solution builds with zero warnings
    Tool: Bash
    Preconditions: All .csproj files created with correct references
    Steps:
      1. Run: dotnet restore Hex.Scaffold.sln
      2. Run: dotnet build Hex.Scaffold.sln --no-restore
      3. Assert: exit code 0
      4. Assert: output contains "Build succeeded"
      5. Assert: output contains "0 Warning(s)"
      6. Assert: output contains "0 Error(s)"
    Expected Result: Clean build with zero warnings and zero errors
    Failure Indicators: Any warning or error in build output
    Evidence: .sisyphus/evidence/task-1-build-success.txt

  Scenario: Project references follow hexagonal rules
    Tool: Bash (grep)
    Preconditions: All .csproj files exist
    Steps:
      1. Verify Domain.csproj has ZERO <ProjectReference> elements
      2. Verify Application.csproj references ONLY Domain
      3. Verify Adapters.Inbound.csproj references ONLY Application (Domain transitive)
      4. Verify Adapters.Outbound.csproj references ONLY Domain
      5. Verify Adapters.Persistence.csproj references ONLY Domain and Application
      6. Verify Api.csproj references all 5 source projects
    Expected Result: All dependency rules match the table
    Failure Indicators: Any unexpected ProjectReference
    Evidence: .sisyphus/evidence/task-1-dependency-check.txt
  ```

  **Commit**: YES
  - Message: `chore(scaffold): initialize solution with hexagonal project structure`
  - Files: `Hex.Scaffold.sln`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, all `.csproj`, all `GlobalUsings.cs`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 2. Domain Base Types & Port Interfaces

  **What to do**:
  - Create `GlobalUsings.cs` for Domain with: `Ardalis.GuardClauses`, `Ardalis.Result`, `Ardalis.SharedKernel`, `Ardalis.SmartEnum`, `Ardalis.Specification`, `Mediator`, `Microsoft.Extensions.Logging`
  - Create port interfaces in `Domain/Ports/Outbound/`:
    - `IEventPublisher.cs`: `ValueTask PublishAsync<TEvent>(string topic, TEvent @event, CancellationToken ct)` where TEvent can be any serializable object
    - `IExternalApiClient.cs`: `Task<Result<TResponse>> SendAsync<TResponse>(string endpoint, HttpMethod method, object? body, CancellationToken ct)` — generic HTTP client port
    - `ICacheService.cs`: `Task<T?> GetAsync<T>(string key, CancellationToken ct)`, `Task SetAsync<T>(string key, T value, TimeSpan? expiration, CancellationToken ct)`, `Task RemoveAsync(string key, CancellationToken ct)`
    - `ISampleReadModelRepository.cs`: `Task UpsertAsync(SampleReadModel document, CancellationToken ct)`, `Task DeleteAsync(string id, CancellationToken ct)` — note: SampleReadModel is a simple POCO defined in this interface file or a separate file, not the aggregate
  - Create `Domain/Interfaces/IDeleteSampleService.cs`: `ValueTask<Result> DeleteSample(SampleId id)`
  - Create `Domain/Ports/Outbound/SampleReadModel.cs`: simple record/class with `string Id`, `string Name`, `string Status`, `DateTime LastUpdated` — this is the MongoDB document shape, defined as a port contract
  - **Do NOT create aggregate, VOs, events yet** — that's Task 3

  **Must NOT do**:
  - Do NOT add any infrastructure dependencies (no EF Core, no MongoDB.Driver, no Kafka)
  - Do NOT create the Sample aggregate — only port interfaces and base types
  - Port interfaces MUST use only primitive types or types defined in Domain

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Small files with interface definitions — straightforward
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Defines interface patterns (IDeleteContributorService), naming conventions, and Core/Interfaces location

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Task 1 if Task 1 completes first — practically sequential)
  - **Parallel Group**: Wave 1 (after Task 1)
  - **Blocks**: Tasks 3-8
  - **Blocked By**: Task 1

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/Interfaces/IDeleteContributorService.cs` — Domain service interface pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/Interfaces/IEmailSender.cs` — Outbound port interface pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/GlobalUsings.cs` — GlobalUsings pattern for Core/Domain layer
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/List/IListContributorsQueryService.cs` — Query service interface pattern (but this one goes in Application, not Domain)

  **WHY Each Reference Matters**:
  - IDeleteContributorService: Shows the exact pattern for domain service interfaces (ValueTask<Result> return)
  - IEmailSender: Shows an outbound port interface defined in Core — replicate pattern for IEventPublisher, ICacheService, etc.
  - GlobalUsings: Exact set of global usings for the Domain layer

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Domain project builds with zero warnings
    Tool: Bash
    Preconditions: Task 1 complete, port interfaces created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Domain/Hex.Scaffold.Domain.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Domain compiles independently with no warnings
    Failure Indicators: Any warning or reference to external projects
    Evidence: .sisyphus/evidence/task-2-domain-build.txt

  Scenario: Port interfaces have no infrastructure dependencies
    Tool: Bash (grep)
    Preconditions: All port interface files created
    Steps:
      1. Search all files in Domain/Ports/ for forbidden namespaces: MongoDB, Confluent, StackExchange, EntityFrameworkCore, System.Net.Http
      2. Assert: zero matches
    Expected Result: Port interfaces are pure domain contracts
    Failure Indicators: Any infrastructure namespace found
    Evidence: .sisyphus/evidence/task-2-no-infra-deps.txt
  ```

  **Commit**: YES
  - Message: `feat(domain): add base types and port interfaces`
  - Files: `Domain/GlobalUsings.cs`, `Domain/Ports/Outbound/*.cs`, `Domain/Interfaces/*.cs`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 3. Domain Sample Aggregate — Entity, Value Objects, Events, Specifications, Services

  **What to do**:
  - Create `Domain/SampleAggregate/SampleId.cs`: Vogen `[ValueObject<int>]` readonly partial struct with validation > 0
  - Create `Domain/SampleAggregate/SampleName.cs`: Vogen `[ValueObject<string>(conversions: Conversions.SystemTextJson)]` with MaxLength=200, NotEmpty validation
  - Create `Domain/SampleAggregate/SampleStatus.cs`: SmartEnum with values: Active(1), Inactive(2), NotSet(3)
  - Create `Domain/SampleAggregate/Sample.cs`: Aggregate root entity inheriting `EntityBase<Sample, SampleId>`, `IAggregateRoot`
    - Primary constructor: `Sample(SampleName name)`
    - Properties: `SampleName Name { get; private set; }`, `SampleStatus Status { get; private set; } = SampleStatus.NotSet`, `string? Description { get; private set; }`
    - Methods: `UpdateName(SampleName newName)` → registers `SampleUpdatedEvent`, returns `this`
    - Methods: `UpdateDescription(string? description)` → returns `this`
    - Methods: `Activate()` / `Deactivate()` → sets Status, registers event, returns `this`
  - Create domain events in `Domain/SampleAggregate/Events/`:
    - `SampleCreatedEvent(Sample sample)` — sealed class, DomainEventBase
    - `SampleUpdatedEvent(Sample sample)` — sealed class, DomainEventBase
    - `SampleDeletedEvent(SampleId sampleId)` — sealed class, DomainEventBase
  - Create event handler in `Domain/SampleAggregate/Handlers/`:
    - `SampleEventPublishHandler.cs`: implements `INotificationHandler<SampleCreatedEvent>`, `INotificationHandler<SampleUpdatedEvent>`, `INotificationHandler<SampleDeletedEvent>`
    - Injects `IEventPublisher` (port) and `ICacheService` (port)
    - On create/update: publish to Kafka via IEventPublisher + invalidate Redis cache via ICacheService
    - On delete: publish to Kafka + remove from Redis cache
  - Create `Domain/SampleAggregate/Specifications/SampleByIdSpec.cs`: `Specification<Sample>` with `Query.Where(s => s.Id == sampleId)`
  - Create `Domain/Services/DeleteSampleService.cs`: implements `IDeleteSampleService`, injects `IRepository<Sample>`, `IMediator`, `ILogger`. Loads entity, deletes from repo, publishes `SampleDeletedEvent`

  **Must NOT do**:
  - Do NOT add EF Core attributes or annotations — entity configuration is in Adapters.Persistence
  - Do NOT reference any adapter project
  - Do NOT add public setters — all `private set`
  - Do NOT throw exceptions for expected failures — use `Result.NotFound()`, `Result.Invalid()`

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Complex domain modeling requiring precise DDD patterns, multiple interdependent files
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Defines exact patterns for aggregates (Phase 1.1), Vogen IDs (1.2), Value Objects (1.3), SmartEnums (1.4), Domain Events (1.5), Event Handlers (1.6), Specifications (1.7), Domain Services (1.8-1.9)

  **Parallelization**:
  - **Can Run In Parallel**: YES (in Wave 2, but must complete before tasks that depend on aggregate types)
  - **Parallel Group**: Wave 2
  - **Blocks**: Tasks 4, 5, 6, 8, 9, 10
  - **Blocked By**: Task 2

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/Contributor.cs` — Aggregate root pattern (EntityBase, IAggregateRoot, primary constructor, private setters, mutation methods, RegisterDomainEvent)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/ContributorId.cs` — Vogen ID pattern (readonly partial struct, Validate > 0)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/ContributorName.cs` — Vogen string value object (MaxLength, validation)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/ContributorStatus.cs` — SmartEnum pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/Events/ContributorNameUpdatedEvent.cs` — Domain event pattern (sealed, DomainEventBase, primary constructor, init properties)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/Handlers/ContributorNameUpdatedEmailNotificationHandler.cs` — Event handler pattern (INotificationHandler, ValueTask Handle, DI via primary constructor)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/Specifications/ContributorByIdSpec.cs` — Specification pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/Services/DeleteContributorService.cs` — Domain service pattern (deletion with event publishing)

  **WHY Each Reference Matters**:
  - Contributor.cs: EXACT pattern for Sample.cs — same base class, same mutation method style, same event registration
  - ContributorId.cs: EXACT pattern for SampleId.cs — same Vogen attribute, same validation
  - Event handler: Shows how to inject outbound ports and call them from domain event handlers

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Domain project builds with Sample aggregate
    Tool: Bash
    Preconditions: Tasks 1-2 complete, all Sample aggregate files created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Domain/Hex.Scaffold.Domain.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Domain compiles with aggregate, VOs, events, specs, services
    Failure Indicators: Any warning, missing type reference, or build error
    Evidence: .sisyphus/evidence/task-3-domain-build.txt

  Scenario: Aggregate has only private setters
    Tool: Bash (grep)
    Preconditions: Sample.cs exists
    Steps:
      1. Search Sample.cs for "public.*set;" (public setter pattern)
      2. Assert: zero matches
      3. Search Sample.cs for "private set;" — should find multiple matches
    Expected Result: All setters are private
    Failure Indicators: Any public setter found
    Evidence: .sisyphus/evidence/task-3-private-setters.txt
  ```

  **Commit**: YES
  - Message: `feat(domain): add Sample aggregate with VOs, events, and specifications`
  - Files: `Domain/SampleAggregate/**`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 4. Application CQRS Commands, Queries, Handlers & Behaviors

  **What to do**:
  - Create `Application/GlobalUsings.cs`: `Ardalis.Result`, `Ardalis.SharedKernel`, `Mediator`
  - Create `Application/Constants.cs`: `DEFAULT_PAGE_SIZE = 10`, `MAX_PAGE_SIZE = 50`
  - Create `Application/PagedResult.cs`: record with `List<T> Items`, `int Page`, `int PerPage`, `int TotalCount`, `int TotalPages`
  - Create `Application/Samples/SampleDto.cs`: `record SampleDto(SampleId Id, SampleName Name, SampleStatus Status, string? Description)`
  - Create CQRS files in `Application/Samples/`:
    - **Create/**: `CreateSampleCommand(SampleName Name, string? Description) : ICommand<Result<SampleId>>` + `CreateSampleHandler(IRepository<Sample> _repository) : ICommandHandler<...>` — creates entity, adds to repo, returns ID
    - **Get/**: `GetSampleQuery(SampleId SampleId) : IQuery<Result<SampleDto>>` + `GetSampleHandler(IReadRepository<Sample> _repository, ICacheService _cache) : IQueryHandler<...>` — cache-aside: check Redis, miss → query via SampleByIdSpec, set cache with 5min TTL, return DTO
    - **List/**: `ListSamplesQuery(int? Page, int? PerPage) : IQuery<Result<PagedResult<SampleDto>>>` + `ListSamplesHandler(IListSamplesQueryService _query) : IQueryHandler<...>` — delegates to query service
    - **List/**: `IListSamplesQueryService.cs`: `Task<PagedResult<SampleDto>> ListAsync(int page, int perPage)`
    - **Update/**: `UpdateSampleCommand(SampleId Id, SampleName Name, string? Description) : ICommand<Result<SampleDto>>` + `UpdateSampleHandler(IRepository<Sample> _repository) : ICommandHandler<...>` — loads, mutates, saves, returns DTO
    - **Delete/**: `DeleteSampleCommand(SampleId SampleId) : ICommand<Result>` + `DeleteSampleHandler(IDeleteSampleService _service) : ICommandHandler<...>` — delegates to domain service
  - Create `Application/Behaviors/LoggingBehavior.cs`: `IPipelineBehavior<TMessage, TResponse>` — logs command/query name, execution time, result status

  **Must NOT do**:
  - Do NOT inject infrastructure types (DbContext, IMongoClient, etc.) — use port interfaces only
  - Commands use `IRepository<T>`, queries use `IReadRepository<T>` or `IQueryService`
  - All handlers return `ValueTask` (not `Task`)
  - Do NOT throw exceptions for expected failures — return `Result.NotFound()`, `Result.Invalid()`

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: CQRS pattern requires precise interface implementation, cache-aside logic, correct Mediator typing
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Phase 2 defines exact command/query/handler patterns, DTO conventions, Mediator configuration

  **Parallelization**:
  - **Can Run In Parallel**: YES (after Task 3, parallel with 5-8)
  - **Parallel Group**: Wave 2
  - **Blocks**: Tasks 9, 10, 11
  - **Blocked By**: Tasks 2, 3

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/Create/CreateContributorCommand.cs` — Command record pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/Create/CreateContributorHandler.cs` — Command handler pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/Get/GetContributorQuery.cs` — Query record pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/Get/GetContributorHandler.cs` — Query handler with Specification
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/List/ListContributorsHandler.cs` — List handler delegating to query service
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/List/IListContributorsQueryService.cs` — Query service interface
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/Delete/DeleteContributorHandler.cs` — Delete handler delegating to domain service
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/Contributors/ContributorDTO.cs` — DTO record pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.UseCases/PagedResult.cs` — Pagination result record

  **WHY Each Reference Matters**:
  - Each handler file shows the EXACT Mediator interface to implement (ICommandHandler<T,R> with ValueTask<R>)
  - GetContributorHandler: Shows cache-aside-like pattern with Specification — adapt to add Redis ICacheService
  - Delete handler: Shows delegation to domain service pattern

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Application project builds with all CQRS handlers
    Tool: Bash
    Preconditions: Tasks 1-3 complete, all command/query/handler files created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Application/Hex.Scaffold.Application.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Application compiles with all handlers resolving Domain types
    Failure Indicators: Missing type references, wrong Mediator interfaces
    Evidence: .sisyphus/evidence/task-4-application-build.txt

  Scenario: No infrastructure types in Application layer
    Tool: Bash (grep)
    Preconditions: Application project files exist
    Steps:
      1. Search all .cs files in Application/ for: "EntityFrameworkCore", "MongoDB", "StackExchange", "Confluent", "Npgsql", "DbContext"
      2. Assert: zero matches
    Expected Result: Application layer has zero infrastructure references
    Failure Indicators: Any infrastructure namespace found
    Evidence: .sisyphus/evidence/task-4-no-infra-deps.txt
  ```

  **Commit**: YES
  - Message: `feat(application): add CQRS commands, queries, and handlers for Sample`
  - Files: `Application/**`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 5. Adapters.Persistence — PostgreSQL (EF Core + Dapper)

  **What to do**:
  - Create `Adapters.Persistence/GlobalUsings.cs`: EntityFrameworkCore, Npgsql, Ardalis.Specification.EntityFrameworkCore, Microsoft.Extensions.DependencyInjection, etc.
  - Create `Adapters.Persistence/PostgreSql/AppDbContext.cs`: inherits `DbContext`, `DbSet<Sample> Samples => Set<Sample>()`, `OnModelCreating` applies configurations from assembly
  - Create `Adapters.Persistence/PostgreSql/EfRepository.cs`: `EfRepository<T>(AppDbContext dbContext) : RepositoryBase<T>(dbContext), IReadRepository<T>, IRepository<T> where T : class, IAggregateRoot`
  - Create `Adapters.Persistence/PostgreSql/EventDispatcherInterceptor.cs`: `SaveChangesInterceptor` that dispatches domain events after SaveChanges via `IDomainEventDispatcher` (copy pattern from reference)
  - Create `Adapters.Persistence/PostgreSql/Config/SampleConfiguration.cs`: `IEntityTypeConfiguration<Sample>` with:
    - HasKey(x => x.Id)
    - Vogen ID conversion (Id.Value ↔ SampleId.From)
    - Vogen SampleName conversion with MaxLength
    - SmartEnum SampleStatus conversion (Value ↔ FromValue)
  - Create `Adapters.Persistence/PostgreSql/Queries/ListSamplesQueryService.cs`: implements `IListSamplesQueryService` using **Dapper** (not EF Core) for list queries — `SELECT Id, Name, Status, Description FROM Samples ORDER BY Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY` + count query. Maps raw results to `SampleDto` manually.
  - Create `Adapters.Persistence/Extensions/PostgreSqlServiceExtensions.cs`: extension method `AddPostgreSqlServices(this IServiceCollection, IConfiguration config)` — registers AppDbContext with Npgsql provider (`EnableRetryOnFailure`), EventDispatchInterceptor, IDomainEventDispatcher as MediatorDomainEventDispatcher, IRepository<> and IReadRepository<> as EfRepository<>, IListSamplesQueryService
  - **Dapper connection**: Inject `IConfiguration` to get connection string, use `NpgsqlConnection` directly in query service

  **Must NOT do**:
  - Do NOT reference Adapters.Inbound or Adapters.Outbound
  - Do NOT create migrations yet (they require the Api project to be the startup project)
  - Do NOT use EF Core for the list query — use Dapper to demonstrate coexistence

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: EF Core configuration with Vogen converters, Dapper queries, and interceptor pattern requires careful implementation
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Phase 4 defines EF Core configuration patterns (4.1), query service implementation (4.2), DI registration (4.3), Vogen EF Core converters

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Tasks 4, 6, 7, 8)
  - **Parallel Group**: Wave 2
  - **Blocks**: Tasks 9, 11, 16
  - **Blocked By**: Tasks 2, 3

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/Data/AppDbContext.cs` — DbContext pattern with ApplyConfigurationsFromAssembly
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/Data/EfRepository.cs` — Generic repository pattern (RepositoryBase<T>)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/Data/EventDispatcherInterceptor.cs` — Domain event dispatch interceptor (SaveChangesInterceptor)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/Data/Config/ContributorConfiguration.cs` — Entity configuration with Vogen conversions
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/Data/Queries/ListContributorsQueryService.cs` — Query service pattern (adapt from EF Core to Dapper)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/InfrastructureServiceExtensions.cs` — DI registration pattern for EF Core + repositories

  **WHY Each Reference Matters**:
  - AppDbContext: EXACT pattern to replicate — minimal DbContext with automatic config discovery
  - EventDispatcherInterceptor: Critical for domain event flow — must dispatch AFTER SaveChanges succeeds
  - ContributorConfiguration: Shows Vogen value object EF Core conversions — replicate for SampleId, SampleName, SampleStatus
  - ListContributorsQueryService: Base pattern — but change from EF Core to Dapper for the list implementation

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Persistence project builds with PostgreSQL adapter
    Tool: Bash
    Preconditions: Tasks 1-3 complete, all PostgreSQL files created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Persistence compiles with EF Core, Dapper, and repository implementations
    Evidence: .sisyphus/evidence/task-5-persistence-build.txt

  Scenario: Dapper query service uses raw SQL, not EF Core
    Tool: Bash (grep)
    Preconditions: ListSamplesQueryService.cs exists
    Steps:
      1. Search ListSamplesQueryService.cs for "NpgsqlConnection" or "IDbConnection" — should find
      2. Search ListSamplesQueryService.cs for "DbContext" or "_db." — should NOT find
    Expected Result: List query uses Dapper, not EF Core
    Evidence: .sisyphus/evidence/task-5-dapper-check.txt
  ```

  **Commit**: YES
  - Message: `feat(persistence): add PostgreSQL adapter with EF Core and Dapper`
  - Files: `Adapters.Persistence/PostgreSql/**`, `Adapters.Persistence/Extensions/PostgreSqlServiceExtensions.cs`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 6. Adapters.Persistence — MongoDB (Read Model Repository)

  **What to do**:
  - Create `Adapters.Persistence/MongoDb/SampleDocument.cs`: MongoDB document class with `[BsonId]` `ObjectId Id`, `int SampleId`, `string Name`, `string Status`, `string? Description`, `DateTime LastUpdated`
  - Create `Adapters.Persistence/MongoDb/SampleReadModelRepository.cs`: implements `ISampleReadModelRepository` from Domain ports
    - Injects `IMongoClient`, `IOptions<MongoDbOptions>`
    - Gets database and collection from options
    - `UpsertAsync`: uses `ReplaceOneAsync` with `IsUpsert = true`, filter by `SampleId`
    - `DeleteAsync`: uses `DeleteOneAsync` by SampleId
  - Create `Adapters.Persistence/MongoDb/Serializers/VogenBsonSerializers.cs`: Custom BSON serializers for `SampleId` and `SampleName` Vogen types — serialize to underlying primitive (int/string) and back
  - Create `Adapters.Persistence/Extensions/MongoDbServiceExtensions.cs`: `AddMongoDbServices(IServiceCollection, IConfiguration)` — registers IMongoClient (singleton with connection settings), ISampleReadModelRepository, registers BSON serializers via `BsonSerializer.RegisterSerializer()`
  - MongoDbOptions class: `ConnectionString`, `DatabaseName` properties

  **Must NOT do**:
  - Do NOT create indexes, sharding, or change streams
  - Do NOT reference Adapters.Inbound or Adapters.Outbound
  - MongoDB is for the READ MODEL only — not for the primary aggregate store

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Vogen BSON serializers are non-trivial and error-prone
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Vogen value object patterns (1.2, 1.3) inform how to serialize/deserialize these types

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Tasks 4, 5, 7, 8)
  - **Parallel Group**: Wave 2
  - **Blocks**: Tasks 10, 11, 16
  - **Blocked By**: Tasks 2, 3

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/ContributorAggregate/ContributorId.cs` — Vogen type to understand underlying value access (.Value property, .From() factory)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/InfrastructureServiceExtensions.cs` — DI extension method pattern

  **External References**:
  - MongoDB.Driver official docs: `https://www.mongodb.com/docs/drivers/csharp/current/` — MongoClient setup, BSON serialization
  - Vogen docs: `https://github.com/SteveDunn/Vogen` — Understanding generated .Value property and .From() factory

  **WHY Each Reference Matters**:
  - ContributorId.cs: Need to understand Vogen's generated API (.Value for read, .From() for create) to write BSON serializers correctly
  - InfrastructureServiceExtensions: DI pattern to follow for MongoDB registration

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Persistence project builds with MongoDB adapter
    Tool: Bash
    Preconditions: MongoDB files created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Compiles with MongoDB.Driver and custom BSON serializers
    Evidence: .sisyphus/evidence/task-6-mongodb-build.txt

  Scenario: MongoDB adapter does not reference other adapters
    Tool: Bash (grep)
    Preconditions: MongoDb/ directory exists
    Steps:
      1. Search all files in MongoDb/ for: "Adapters.Inbound", "Adapters.Outbound", "FastEndpoints", "Confluent.Kafka"
      2. Assert: zero matches
    Expected Result: MongoDB adapter only references Domain types
    Evidence: .sisyphus/evidence/task-6-no-cross-adapter.txt
  ```

  **Commit**: YES
  - Message: `feat(persistence): add MongoDB adapter with read model repository`
  - Files: `Adapters.Persistence/MongoDb/**`, `Adapters.Persistence/Extensions/MongoDbServiceExtensions.cs`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 7. Adapters.Persistence — Redis Cache

  **What to do**:
  - Create `Adapters.Persistence/Redis/RedisCacheService.cs`: implements `ICacheService` from Domain ports
    - Injects `IConnectionMultiplexer`, `ILogger`
    - `GetAsync<T>`: `StringGetAsync` → `JsonSerializer.Deserialize<T>` (with Vogen-aware JsonSerializerOptions)
    - `SetAsync<T>`: `JsonSerializer.Serialize` → `StringSetAsync` with optional TTL
    - `RemoveAsync`: `KeyDeleteAsync`
    - Graceful degradation: catch `RedisConnectionException`, log warning, return default (cache miss)
  - Create `Adapters.Persistence/Extensions/RedisServiceExtensions.cs`: `AddRedisServices(IServiceCollection, IConfiguration)` — registers IConnectionMultiplexer (singleton, `AbortOnConnectFail = false`), ICacheService as RedisCacheService (scoped)
  - RedisOptions class: `ConnectionString` property
  - **JsonSerializerOptions**: Configure with Vogen SystemTextJson converters for SampleId, SampleName so cached DTOs serialize correctly

  **Must NOT do**:
  - Do NOT implement Redis Pub/Sub, Streams, Lua scripts, or distributed locking
  - Do NOT crash if Redis is unavailable — graceful degradation (log + return null/default)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: JSON serialization with Vogen types needs careful configuration
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Vogen value object SystemTextJson conversion pattern

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Tasks 4, 5, 6, 8)
  - **Parallel Group**: Wave 2
  - **Blocks**: Task 11
  - **Blocked By**: Task 2

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Infrastructure/InfrastructureServiceExtensions.cs` — DI extension method pattern

  **External References**:
  - StackExchange.Redis: `https://stackexchange.github.io/StackExchange.Redis/` — IConnectionMultiplexer pattern
  - Vogen SystemTextJson: Vogen generates STJ converters when `Conversions.SystemTextJson` is set

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Redis adapter builds and compiles
    Tool: Bash
    Preconditions: Redis files created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Compiles with StackExchange.Redis
    Evidence: .sisyphus/evidence/task-7-redis-build.txt

  Scenario: Redis adapter handles connection failure gracefully
    Tool: Bash (grep)
    Preconditions: RedisCacheService.cs exists
    Steps:
      1. Search for try/catch blocks handling RedisConnectionException or RedisException
      2. Assert: at least 1 match in GetAsync and SetAsync
    Expected Result: Graceful degradation on Redis failure
    Evidence: .sisyphus/evidence/task-7-graceful-degradation.txt
  ```

  **Commit**: YES
  - Message: `feat(persistence): add Redis cache adapter`
  - Files: `Adapters.Persistence/Redis/**`, `Adapters.Persistence/Extensions/RedisServiceExtensions.cs`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 8. Adapters.Outbound — Kafka Producer & Resilient HTTP Client

  **What to do**:
  - Create `Adapters.Outbound/GlobalUsings.cs`
  - Create `Adapters.Outbound/Messaging/KafkaEventPublisher.cs`: implements `IEventPublisher` from Domain ports
    - Injects `IProducer<string, string>` (Confluent.Kafka), `ILogger`
    - `PublishAsync<TEvent>`: serializes event to JSON, produces to topic with event type as key
    - Graceful error handling: catch `ProduceException`, log error, do NOT throw (eventual consistency — see guardrails)
    - Add `// TODO: Implement Transactional Outbox pattern for production use` comment
  - Create `Adapters.Outbound/Http/ExternalApiClient.cs`: implements `IExternalApiClient` from Domain ports
    - Injects `IHttpClientFactory` (named client "ExternalApi"), `ILogger`
    - `SendAsync<TResponse>`: creates HttpRequestMessage, sends via client, deserializes response
    - Returns `Result<TResponse>` — maps HTTP errors to Result.Error, 404 to Result.NotFound
  - **Note**: The IProducer<string,string> and IHttpClientFactory are registered in the Api composition root (Task 11/12), NOT here. This project only provides implementations.

  **Must NOT do**:
  - Do NOT reference Application, Adapters.Inbound, or Adapters.Persistence
  - Do NOT implement Schema Registry, Avro, or Protobuf — simple JSON only
  - Do NOT register DI services here — that's the Api project's job (composition root)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Kafka producer and resilient HTTP client patterns need correct error handling
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Port/adapter pattern, Result<T> error handling

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Tasks 4, 5, 6, 7)
  - **Parallel Group**: Wave 2
  - **Blocks**: Tasks 10, 11
  - **Blocked By**: Tasks 2, 3

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Core/Interfaces/IEmailSender.cs` — Outbound port interface pattern (reference already defined in Domain)

  **External References**:
  - Confluent.Kafka .NET: `https://docs.confluent.io/kafka-clients/dotnet/current/overview.html` — Producer pattern
  - Microsoft.Extensions.Http.Resilience: `https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience` — Standard resilience handler

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Outbound adapter builds with Kafka and HTTP client
    Tool: Bash
    Preconditions: Outbound files created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Adapters.Outbound/Hex.Scaffold.Adapters.Outbound.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Compiles with Confluent.Kafka and HTTP resilience
    Evidence: .sisyphus/evidence/task-8-outbound-build.txt

  Scenario: Outbound adapter references only Domain
    Tool: Bash (grep)
    Preconditions: Outbound project files exist
    Steps:
      1. Check Adapters.Outbound.csproj for ProjectReference — should only reference Domain
      2. Search .cs files for "Adapters.Inbound", "Adapters.Persistence", "Application" namespaces
      3. Assert: zero matches
    Expected Result: Outbound adapter only depends on Domain
    Evidence: .sisyphus/evidence/task-8-dependency-check.txt
  ```

  **Commit**: YES
  - Message: `feat(outbound): add Kafka producer and resilient HTTP client adapters`
  - Files: `Adapters.Outbound/**`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 9. Adapters.Inbound — FastEndpoints CRUD for Sample

  **What to do**:
  - Create `Adapters.Inbound/GlobalUsings.cs`: `FastEndpoints`, `Ardalis.Result`, `Mediator`
  - Create `Adapters.Inbound/Api/Extensions/ResultExtensions.cs`: Copy and adapt from reference — `ToCreatedResult()`, `ToGetByIdResult()`, `ToUpdateResult()`, `ToDeleteResult()`, `ToOkOnlyResult()` mapping `Result<T>` to TypedResults
  - Create `Adapters.Inbound/Api/Samples/SampleRecord.cs`: `record SampleRecord(int Id, string Name, string Status, string? Description)` — API-facing response DTO
  - Create CRUD endpoints in `Adapters.Inbound/Api/Samples/`:
    - **Create.cs**: `POST /samples` → `CreateSampleCommand` → `Results<Created<CreateSampleResponse>, ValidationProblem, ProblemHttpResult>`
    - **Create.CreateSampleRequest.cs**: `Route = "/Samples"`, `string Name`, `string? Description`
    - **Create.CreateSampleValidator.cs**: `Validator<CreateSampleRequest>` — Name required, min 2, max SampleName.MaxLength
    - **Create.CreateSampleResponse.cs**: `int Id`, `string Name`
    - **GetById.cs**: `GET /samples/{sampleId:int}` → `GetSampleQuery` → `Results<Ok<SampleRecord>, NotFound, ProblemHttpResult>`
    - **GetById.GetSampleByIdRequest.cs**: `int SampleId`
    - **GetById.GetSampleByIdMapper.cs**: `Mapper<..., SampleRecord, SampleDto>` with `FromEntity` mapping Vogen values
    - **List.cs**: `GET /samples` → `ListSamplesQuery` → pagination with Link header
    - **List.ListSamplesRequest.cs**: `int? Page`, `int? PerPage`
    - **List.ListSamplesMapper.cs**: Maps `SampleDto` → `SampleRecord`
    - **Update.cs**: `PUT /samples/{sampleId:int}` → `UpdateSampleCommand`
    - **Update.UpdateSampleRequest.cs**, **Update.UpdateSampleValidator.cs**, **Update.UpdateSampleMapper.cs**
    - **Delete.cs**: `DELETE /samples/{sampleId:int}` → `DeleteSampleCommand`
    - **Delete.DeleteSampleRequest.cs**
  - Each endpoint: `Configure()` with route, `AllowAnonymous()`, `Summary()`, `Tags("Samples")`, `Description()`
  - Value object conversion at boundary: `SampleName.From(request.Name!)`, `SampleId.From(request.SampleId)` in endpoint, NOT in handler

  **Must NOT do**:
  - Do NOT reference Adapters.Outbound or Adapters.Persistence
  - Do NOT access repositories directly — use Mediator.Send() only
  - Do NOT implement authentication — `AllowAnonymous()` on all endpoints

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Many interrelated files (endpoints, requests, validators, responses, mappers) with precise FastEndpoints patterns
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Phase 3 defines exact FastEndpoints patterns (REPR, Configure, Summary, Tags, ResultExtensions)

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Task 10 in Wave 3)
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 11
  - **Blocked By**: Tasks 4, 5

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Contributors/Create.cs` — Complete Create endpoint pattern (Configure + ExecuteAsync + Result mapping)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Contributors/GetById.cs` — GetById with Mapper pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Contributors/List.cs` — List with pagination and Link header
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Contributors/Update.cs` — Update endpoint pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Contributors/Delete.cs` — Delete endpoint pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Extensions/ResultExtensions.cs` — Result<T> to TypedResults mapping extensions

  **WHY Each Reference Matters**:
  - Each reference is the EXACT file to replicate — same endpoint structure, same Configure pattern, same Result mapping. Adapt naming from Contributor → Sample.

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Inbound adapter builds with all endpoints
    Tool: Bash
    Preconditions: All endpoint files created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Adapters.Inbound/Hex.Scaffold.Adapters.Inbound.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: All endpoints compile with FastEndpoints
    Evidence: .sisyphus/evidence/task-9-inbound-build.txt

  Scenario: Inbound adapter does not reference persistence or outbound
    Tool: Bash (grep)
    Preconditions: Inbound project exists
    Steps:
      1. Check Adapters.Inbound.csproj — should reference ONLY Application
      2. Search .cs files for "Adapters.Outbound", "Adapters.Persistence", "DbContext", "IMongoClient"
      3. Assert: zero matches
    Expected Result: Strict inbound adapter isolation
    Evidence: .sisyphus/evidence/task-9-isolation-check.txt
  ```

  **Commit**: YES
  - Message: `feat(inbound): add FastEndpoints CRUD for Sample`
  - Files: `Adapters.Inbound/Api/**`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 10. Adapters.Inbound — Kafka Consumer BackgroundService

  **What to do**:
  - Create `Adapters.Inbound/Messaging/SampleEventConsumer.cs`: `BackgroundService` that consumes from Kafka topic "sample-events"
    - Injects `IConsumer<string, string>`, `IServiceScopeFactory`, `ILogger`
    - `ExecuteAsync`: runs consumer loop on `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)`
    - Consumer loop: `Subscribe(["sample-events"])`, `Consume(stoppingToken)`, deserialize JSON to determine event type (created/updated/deleted)
    - On created/updated: resolve `ISampleReadModelRepository` from scope, call `UpsertAsync()` with mapped `SampleReadModel`
    - On deleted: resolve `ISampleReadModelRepository` from scope, call `DeleteAsync()`
    - Manual commit after successful processing (`Consumer.Commit(consumeResult)`)
    - Error handling: catch `ConsumeException` → log, continue. Catch JSON deserialization errors → log, skip message.
    - Add `// TODO: Implement dead-letter topic for failed messages` comment

  **Must NOT do**:
  - Do NOT implement Schema Registry, Avro, or Protobuf
  - Do NOT implement exactly-once semantics — at-least-once with idempotent upsert is sufficient
  - Do NOT reference Adapters.Outbound or Adapters.Persistence directly — resolve from DI scope

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: BackgroundService with Kafka consumer requires careful lifecycle and error handling
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Not directly relevant but provides coding style and error handling patterns

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Task 9)
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 11
  - **Blocked By**: Tasks 4, 6, 8

  **References**:

  **External References**:
  - Confluent.Kafka .NET Consumer: `https://docs.confluent.io/kafka-clients/dotnet/current/overview.html` — Consumer loop pattern

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Inbound adapter builds with Kafka consumer
    Tool: Bash
    Preconditions: SampleEventConsumer.cs created
    Steps:
      1. Run: dotnet build src/Hex.Scaffold.Adapters.Inbound/Hex.Scaffold.Adapters.Inbound.csproj
      2. Assert: exit code 0, zero warnings
    Expected Result: Consumer compiles with Confluent.Kafka
    Evidence: .sisyphus/evidence/task-10-consumer-build.txt

  Scenario: Consumer handles deserialization errors
    Tool: Bash (grep)
    Preconditions: SampleEventConsumer.cs exists
    Steps:
      1. Search for try/catch handling JsonException or deserialization errors
      2. Assert: at least 1 error handling block found
    Expected Result: Consumer doesn't crash on malformed messages
    Evidence: .sisyphus/evidence/task-10-error-handling.txt
  ```

  **Commit**: YES
  - Message: `feat(inbound): add Kafka consumer for Sample events`
  - Files: `Adapters.Inbound/Messaging/**`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 11. Api — Composition Root (Program.cs, DI Wiring, Middleware)

  **What to do**:
  - Create `Api/GlobalUsings.cs`: Serilog, FastEndpoints, Mediator, EntityFrameworkCore
  - Create `Api/Options/KafkaOptions.cs`: `BootstrapServers` string property
  - Create `Api/Options/MongoDbOptions.cs`: `ConnectionString`, `DatabaseName`
  - Create `Api/Options/RedisOptions.cs`: `ConnectionString`
  - Create `Api/Options/ExternalApiOptions.cs`: `BaseUrl`
  - Create `Api/Configurations/ServiceConfigs.cs`: extension method `AddServiceConfigs()` that chains:
    - `AddPostgreSqlServices()` (from Adapters.Persistence)
    - `AddMongoDbServices()` (from Adapters.Persistence)
    - `AddRedisServices()` (from Adapters.Persistence)
    - Kafka producer registration: `AddSingleton<IProducer<string,string>>` with config from `KafkaOptions`
    - Kafka consumer registration: `AddSingleton<IConsumer<string,string>>` with config from `KafkaOptions`
    - `AddHostedService<SampleEventConsumer>()` (Kafka consumer BackgroundService)
    - `IEventPublisher → KafkaEventPublisher` (scoped)
    - `IExternalApiClient → ExternalApiClient` (scoped)
    - HTTP client with resilience: `AddHttpClient("ExternalApi").AddStandardResilienceHandler()`
    - Scrutor assembly scanning for remaining port implementations
  - Create `Api/Configurations/MediatorConfig.cs`: `AddMediator()` with source generator config, assemblies from Domain + Application + Adapters.Persistence + Adapters.Outbound + Adapters.Inbound + Api, pipeline behaviors: `LoggingBehavior<,>`
  - Create `Api/Configurations/MiddlewareConfig.cs`: extension method for middleware pipeline:
    - Development: `UseDeveloperExceptionPage()`
    - Production: `UseExceptionHandler()` + `UseStatusCodePages()` (built-in ProblemDetails)
    - `UseFastEndpoints()` with endpoint configuration
    - Swagger/Scalar in development
    - HTTPS redirection
    - Database migration (conditional on config `Database:ApplyMigrationsOnStartup`)
  - Create `Api/Program.cs`: Wire everything together following reference pattern:
    - `builder.Services.AddProblemDetails()` (built-in .NET, NOT Hellang)
    - `AddServiceConfigs()`, `AddMediatorSourceGen()`, `AddFastEndpoints().SwaggerDocument()`
    - Build app, apply middleware, run
    - Add `public partial class Program { }` for test factory
  - Create `Api/appsettings.json`: all configuration sections (ConnectionStrings for PostgreSQL, Kafka, MongoDB, Redis, ExternalApi, Serilog, Database)
  - Create `Api/appsettings.Development.json`: development-specific overrides (localhost URLs)
  - Create `Api/appsettings.Testing.json`: testing overrides

  **Must NOT do**:
  - Do NOT add authentication beyond a TODO comment: `// TODO: Add authentication/authorization middleware`
  - Do NOT use Hellang.Middleware.ProblemDetails — use built-in `AddProblemDetails()`
  - Do NOT add Aspire (no ServiceDefaults, no AppHost references)

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Composition root wires everything together — requires understanding of all adapter registrations and correct ordering
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: ServiceConfigs, MediatorConfig, MiddlewareConfig, OptionConfigs patterns from the reference

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on all adapter tasks completing)
  - **Parallel Group**: Wave 3 (after Tasks 5-10)
  - **Blocks**: Tasks 12, 13, 16, 17
  - **Blocked By**: Tasks 5, 6, 7, 8, 9, 10

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Program.cs` — Program.cs pattern (builder → services → app → middleware → run)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Configurations/ServiceConfigs.cs` — Service chain pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Configurations/MediatorConfig.cs` — Mediator config with assembly scanning
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Configurations/MiddlewareConfig.cs` — Middleware pipeline pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Configurations/OptionConfigs.cs` — Options pattern
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/appsettings.json` — Config structure

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Full solution builds with composition root
    Tool: Bash
    Preconditions: All adapter tasks complete, Api wiring done
    Steps:
      1. Run: dotnet build Hex.Scaffold.sln
      2. Assert: exit code 0, zero warnings, zero errors
    Expected Result: Complete solution compiles end-to-end
    Evidence: .sisyphus/evidence/task-11-full-build.txt

  Scenario: Program.cs does not use Hellang or Aspire
    Tool: Bash (grep)
    Preconditions: Program.cs and ServiceConfigs exist
    Steps:
      1. Search all Api/ .cs files for "Hellang", "Aspire", "ServiceDefaults"
      2. Assert: zero matches
    Expected Result: No forbidden dependencies
    Evidence: .sisyphus/evidence/task-11-no-forbidden-deps.txt
  ```

  **Commit**: YES
  - Message: `feat(api): wire composition root with DI and middleware`
  - Files: `Api/**`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 12. Api — Observability, Health Checks, Rate Limiting, OpenAPI

  **What to do**:
  - Create `Api/Configurations/ObservabilityConfig.cs`: extension method `AddObservability(IHostApplicationBuilder, string serviceName)`:
    - `AddOpenTelemetry()` with `.ConfigureResource()` (service name, environment, version)
    - `.WithTracing()`: AddAspNetCoreInstrumentation (filter out /healthz, /ready), AddHttpClientInstrumentation, AddSource("Hex.Scaffold.*"), AddOtlpExporter
    - `.WithMetrics()`: AddAspNetCoreInstrumentation, AddHttpClientInstrumentation, AddRuntimeInstrumentation, AddOtlpExporter
    - `builder.Logging.AddOpenTelemetry()` with OTLP exporter
    - Serilog setup: `UseSerilog()` with console sink + OpenTelemetry integration
  - Create `Api/Configurations/HealthCheckConfig.cs`: extension method `AddHealthCheckServices()`:
    - Self check: `AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])`
    - PostgreSQL: `AddNpgSql(connectionString, tags: ["ready"])` (or custom check via DbContext)
    - MongoDB: Custom `MongoDbHealthCheck : IHealthCheck` — ping database
    - Redis: Custom `RedisHealthCheck : IHealthCheck` — ping server
    - Map endpoints: `/healthz` (liveness — only "live" tagged), `/ready` (readiness — all tags including "ready")
  - Create `Api/Configurations/RateLimitingConfig.cs`: extension method `AddRateLimitingServices()`:
    - Fixed-window rate limiter on API endpoints
    - Default: 100 requests per minute per endpoint
    - Configure via `AddRateLimiter()` with `AddFixedWindowLimiter()`
  - Update `Program.cs` to call all new configs: `AddObservability("Hex.Scaffold")`, `AddHealthCheckServices()`, `AddRateLimitingServices()`
  - Update `MiddlewareConfig.cs`: map health check endpoints, `UseRateLimiter()`
  - Swagger document configuration: title "Hex.Scaffold API", version "v1", description, Scalar UI

  **Must NOT do**:
  - Do NOT add custom OpenTelemetry spans or metrics — auto-instrumentation only
  - Do NOT add sliding window or token bucket — fixed-window only
  - Do NOT add health check UI or history storage

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Cross-cutting concerns with OpenTelemetry, health checks, rate limiting configuration
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: LoggerConfigs pattern for Serilog setup

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Task 13)
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 17
  - **Blocked By**: Task 11

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.ServiceDefaults/Extensions.cs` — OpenTelemetry + Health Checks pattern (adapt without Aspire dependency)
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/src/Clean.Architecture.Web/Configurations/LoggerConfigs.cs` — Serilog configuration pattern

  **External References**:
  - OpenTelemetry .NET: `https://opentelemetry.io/docs/languages/dotnet/` — OTLP setup
  - ASP.NET Core Rate Limiting: `https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit` — Fixed window pattern

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Solution builds with observability and health checks
    Tool: Bash
    Preconditions: All config files created, Program.cs updated
    Steps:
      1. Run: dotnet build Hex.Scaffold.sln
      2. Assert: exit code 0, zero warnings
    Expected Result: Full build with all cross-cutting concerns
    Evidence: .sisyphus/evidence/task-12-build.txt

  Scenario: Health check endpoints are mapped
    Tool: Bash (grep)
    Preconditions: HealthCheckConfig.cs and MiddlewareConfig.cs exist
    Steps:
      1. Search for "MapHealthChecks" in Api/ files
      2. Assert: found "/healthz" and "/ready" mappings
    Expected Result: Both liveness and readiness endpoints configured
    Evidence: .sisyphus/evidence/task-12-healthchecks.txt
  ```

  **Commit**: YES
  - Message: `feat(api): add observability, health checks, rate limiting, and OpenAPI`
  - Files: `Api/Configurations/ObservabilityConfig.cs`, `Api/Configurations/HealthCheckConfig.cs`, `Api/Configurations/RateLimitingConfig.cs`, updated `Program.cs`
  - Pre-commit: `dotnet build Hex.Scaffold.sln`

- [x] 13. Dockerfile — Multi-Stage Build for AKS

  **What to do**:
  - Create `Dockerfile` in solution root with multi-stage build:
    - **Stage 1 (restore)**: `mcr.microsoft.com/dotnet/sdk:10.0` AS restore — copy .sln, all .csproj, Directory.*.props, global.json → `dotnet restore`
    - **Stage 2 (build)**: FROM restore AS build — copy all source → `dotnet build -c Release --no-restore`
    - **Stage 3 (publish)**: FROM build AS publish — `dotnet publish src/Hex.Scaffold.Api -c Release -o /app/publish --no-build`
    - **Stage 4 (runtime)**: `mcr.microsoft.com/dotnet/aspnet:10.0` AS final — copy from publish, set `ASPNETCORE_URLS`, `EXPOSE 8080`, `ENTRYPOINT ["dotnet", "Hex.Scaffold.Api.dll"]`
  - Add `.dockerignore` to exclude: `bin/`, `obj/`, `.git/`, `tests/`, `.sisyphus/`, `*.md`
  - Runtime image should be minimal — no SDK, no test projects

  **Must NOT do**:
  - Do NOT include test projects in the Docker image
  - Do NOT create docker-compose.yml or K8s manifests
  - Do NOT bake secrets or connection strings — use environment variables

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Standard multi-stage Dockerfile pattern — well-known and straightforward
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Task 12)
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 17
  - **Blocked By**: Task 11

  **References**:

  **External References**:
  - Microsoft .NET Docker samples: `https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile` — Multi-stage pattern

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Docker image builds successfully
    Tool: Bash
    Preconditions: Dockerfile and .dockerignore created
    Steps:
      1. Run: docker build -t hex-scaffold .
      2. Assert: exit code 0
      3. Run: docker images hex-scaffold --format "{{.Size}}"
      4. Assert: image size < 200MB
    Expected Result: Successful build with small runtime image
    Failure Indicators: Build failure or image > 200MB
    Evidence: .sisyphus/evidence/task-13-docker-build.txt

  Scenario: Docker image does not contain SDK or test projects
    Tool: Bash
    Preconditions: Docker image built
    Steps:
      1. Run: docker run --rm hex-scaffold ls /app/publish/
      2. Assert: contains Hex.Scaffold.Api.dll
      3. Assert: does NOT contain Hex.Scaffold.Tests.* files
      4. Assert: does NOT contain dotnet-sdk
    Expected Result: Minimal runtime image
    Evidence: .sisyphus/evidence/task-13-image-contents.txt
  ```

  **Commit**: YES
  - Message: `feat(docker): add multi-stage Dockerfile for AKS`
  - Files: `Dockerfile`, `.dockerignore`
  - Pre-commit: `docker build -t hex-scaffold .`

- [x] 14. Tests.Architecture — NetArchTest Hexagonal Dependency Rules

  **What to do**:
  - Create `tests/Hex.Scaffold.Tests.Architecture/HexagonalDependencyTests.cs` with xUnit + NetArchTest:
    - **Test: Domain has no project references** — verify Domain types do NOT depend on Application, Adapters.*, or Api namespaces
    - **Test: Application depends only on Domain** — verify Application types depend only on `Hex.Scaffold.Domain` namespace (no Adapter or Api)
    - **Test: Adapters.Inbound does not reference Outbound or Persistence** — verify Inbound types do NOT depend on `Hex.Scaffold.Adapters.Outbound` or `Hex.Scaffold.Adapters.Persistence`
    - **Test: Adapters.Outbound does not reference Application, Inbound, or Persistence** — verify isolation
    - **Test: Adapters.Persistence does not reference Inbound or Outbound** — verify isolation
    - **Test: All entity setters are private** — verify domain entities have no public setters
    - **Test: All command/query handlers return ValueTask** — verify Mediator convention
    - Add `[Trait("Category", "Architecture")]` on all tests for filtering
  - Tests should be exhaustive — check ALL dependency rules from the hexagonal rules table

  **Must NOT do**:
  - Do NOT skip any dependency rule from the table
  - Do NOT make tests flaky — use precise namespace matching

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Requires precise understanding of NetArchTest API and hexagonal dependency rules
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Architecture rules (dependency law table)

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Tasks 15, 16)
  - **Parallel Group**: Wave 4
  - **Blocks**: Task 17
  - **Blocked By**: Task 1 (needs csproj references, but can run after any wave)

  **References**:

  **External References**:
  - NetArchTest: `https://github.com/BenMorris/NetArchTest` — API reference
  - NetArchTest.Rules: NuGet package for fluent architecture testing

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Architecture tests pass
    Tool: Bash
    Preconditions: HexagonalDependencyTests.cs created, all source projects complete
    Steps:
      1. Run: dotnet test tests/Hex.Scaffold.Tests.Architecture --filter "Category=Architecture" -v normal
      2. Assert: exit code 0
      3. Assert: all tests pass (minimum 7 tests)
    Expected Result: All hexagonal dependency rules verified
    Failure Indicators: Any test failure indicates a dependency violation
    Evidence: .sisyphus/evidence/task-14-arch-tests.txt

  Scenario: Architecture tests detect violations (negative test)
    Tool: Bash (grep)
    Preconditions: Test file exists
    Steps:
      1. Verify test checks Domain → Application dependency (should be forbidden)
      2. Verify test checks Inbound → Outbound dependency (should be forbidden)
      3. Count total test methods — should be >= 7
    Expected Result: Comprehensive coverage of all rules
    Evidence: .sisyphus/evidence/task-14-test-coverage.txt
  ```

  **Commit**: YES
  - Message: `test(arch): add NetArchTest hexagonal dependency rules`
  - Files: `tests/Hex.Scaffold.Tests.Architecture/**`
  - Pre-commit: `dotnet test --filter "Category=Architecture"`

- [x] 15. Tests.Unit — Domain & Application Unit Tests

  **What to do**:
  - Create `tests/Hex.Scaffold.Tests.Unit/Domain/SampleAggregateTests.cs`:
    - Test: Creating Sample with valid name sets properties correctly
    - Test: UpdateName with different name registers SampleUpdatedEvent
    - Test: UpdateName with same name does NOT register event
    - Test: Activate/Deactivate changes status and registers event
    - Add `[Trait("Category", "Unit")]` on all tests
  - Create `tests/Hex.Scaffold.Tests.Unit/Application/CreateSampleHandlerTests.cs`:
    - Test: Handle with valid command creates entity and returns SampleId
    - Test: Use NSubstitute to mock IRepository<Sample>
    - Test: Verify repository.AddAsync was called once
    - Add `[Trait("Category", "Unit")]`
  - Create `tests/Hex.Scaffold.Tests.Unit/Application/GetSampleHandlerTests.cs`:
    - Test: Cache hit returns cached DTO without hitting repository
    - Test: Cache miss queries repository and sets cache
    - Test: Entity not found returns Result.NotFound
    - Mock ICacheService and IReadRepository<Sample> via NSubstitute

  **Must NOT do**:
  - Do NOT create more than 3-5 tests per class — this is a scaffold, not full coverage
  - Do NOT test infrastructure implementations (that's integration tests)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Unit tests require mocking with NSubstitute and understanding of domain/application patterns
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Domain patterns to test against

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Tasks 14, 16)
  - **Parallel Group**: Wave 4
  - **Blocks**: Task 17
  - **Blocked By**: Tasks 3, 4

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/tests/Clean.Architecture.UnitTests/` — Unit test structure and patterns

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Unit tests pass
    Tool: Bash
    Preconditions: All unit test files created
    Steps:
      1. Run: dotnet test tests/Hex.Scaffold.Tests.Unit --filter "Category=Unit" -v normal
      2. Assert: exit code 0
      3. Assert: minimum 8 tests pass
    Expected Result: All unit tests green
    Evidence: .sisyphus/evidence/task-15-unit-tests.txt
  ```

  **Commit**: YES
  - Message: `test(unit): add domain and application unit tests`
  - Files: `tests/Hex.Scaffold.Tests.Unit/**`
  - Pre-commit: `dotnet test --filter "Category=Unit"`

- [x] 16. Tests.Integration — TestContainers Integration Tests

  **What to do**:
  - Create `tests/Hex.Scaffold.Tests.Integration/Fixtures/IntegrationTestFixture.cs`:
    - Implements `IAsyncLifetime`
    - Starts TestContainers: PostgreSQL (`Testcontainers.PostgreSql`), MongoDB (`Testcontainers.MongoDb`), Redis (`Testcontainers.Redis`)
    - Creates `WebApplicationFactory<Program>` with real containers as backing services
    - Overrides connection strings in test configuration
    - Runs EF Core migrations on PostgreSQL container
  - Create `tests/Hex.Scaffold.Tests.Integration/Persistence/SampleRepositoryTests.cs`:
    - Test: AddAsync saves entity to PostgreSQL and returns with generated ID
    - Test: GetByIdAsync with Specification returns correct entity
    - Test: RedisCacheService GetAsync/SetAsync roundtrip works correctly
    - Add `[Trait("Category", "Integration")]`
  - All tests use `IClassFixture<IntegrationTestFixture>` for shared container instances

  **Must NOT do**:
  - Do NOT create more than 2-3 integration tests — this is a scaffold demonstrating the pattern
  - Do NOT test Kafka consumer in integration (requires full Kafka container — complex and slow)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: TestContainers setup with multiple containers and WebApplicationFactory is complex
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Testing patterns from reference

  **Parallelization**:
  - **Can Run In Parallel**: YES (parallel with Tasks 14, 15)
  - **Parallel Group**: Wave 4
  - **Blocks**: Task 17
  - **Blocked By**: Tasks 5, 6, 7, 11

  **References**:

  **Pattern References**:
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/tests/Clean.Architecture.FunctionalTests/CustomWebApplicationFactory.cs` — WebApplicationFactory with TestContainers
  - `/Users/lucianosilva/src/open-source/dotnet-clean-arch/tests/Clean.Architecture.IntegrationTests/Data/BaseEfRepoTestFixture.cs` — EF Core test fixture

  **External References**:
  - Testcontainers .NET: `https://dotnet.testcontainers.org/` — Container setup patterns

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Integration tests pass with TestContainers
    Tool: Bash
    Preconditions: Docker running, test files created
    Steps:
      1. Run: dotnet test tests/Hex.Scaffold.Tests.Integration --filter "Category=Integration" -v normal
      2. Assert: exit code 0
      3. Assert: minimum 3 tests pass
    Expected Result: Tests run against real containers
    Failure Indicators: Container startup failure or connection issues
    Evidence: .sisyphus/evidence/task-16-integration-tests.txt
  ```

  **Commit**: YES
  - Message: `test(integration): add TestContainers integration tests`
  - Files: `tests/Hex.Scaffold.Tests.Integration/**`
  - Pre-commit: `dotnet test --filter "Category=Integration"`

- [x] 17. Final Build Verification & Cleanup

  **What to do**:
  - Run `dotnet build Hex.Scaffold.sln` — verify zero warnings, zero errors
  - Run `dotnet test` — verify ALL test categories pass
  - Verify `docker build -t hex-scaffold .` succeeds
  - Review `appsettings.json` — ensure all config sections have sensible defaults
  - Review all `GlobalUsings.cs` — remove any unused imports
  - Add any missing `// TODO:` comments for production hardening:
    - `// TODO: Add authentication/authorization` in middleware
    - `// TODO: Implement Transactional Outbox for domain events → Kafka`
    - `// TODO: Add dead-letter topic handling for Kafka consumer`
    - `// TODO: Configure Redis Cluster for HA`
    - `// TODO: Add MongoDB indexes for read model queries`
  - Verify EF Core migration works: `dotnet ef migrations add InitialCreate -p src/Hex.Scaffold.Adapters.Persistence -s src/Hex.Scaffold.Api -o PostgreSql/Migrations`
  - Clean up any unused files or empty directories

  **Must NOT do**:
  - Do NOT add new features — this is cleanup and verification only
  - Do NOT modify architecture or add new projects

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Verification and cleanup — no new code, just checks
  - **Skills**: [`dotnet-clean-arch`]
    - `dotnet-clean-arch`: Phase 5 verification checklist

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on all previous tasks)
  - **Parallel Group**: Wave 4 (final)
  - **Blocks**: F1-F4 (final verification wave)
  - **Blocked By**: Tasks 12, 13, 14, 15, 16

  **References**:

  **Pattern References**:
  - `dotnet-clean-arch` skill Phase 5: Complete verification checklist to run

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: Full solution verification
    Tool: Bash
    Preconditions: All 16 previous tasks complete
    Steps:
      1. Run: dotnet build Hex.Scaffold.sln
      2. Assert: exit code 0, zero warnings
      3. Run: dotnet test Hex.Scaffold.sln
      4. Assert: exit code 0, all tests pass
      5. Run: docker build -t hex-scaffold .
      6. Assert: exit code 0
    Expected Result: Complete green build, all tests pass, Docker builds
    Evidence: .sisyphus/evidence/task-17-final-verification.txt

  Scenario: EF Core migration creation works
    Tool: Bash
    Preconditions: All persistence code complete
    Steps:
      1. Run: dotnet ef migrations add InitialCreate -p src/Hex.Scaffold.Adapters.Persistence -s src/Hex.Scaffold.Api -o PostgreSql/Migrations
      2. Assert: exit code 0
      3. Verify migration files created in PostgreSql/Migrations/
    Expected Result: Migration generated without errors
    Evidence: .sisyphus/evidence/task-17-migration.txt
  ```

  **Commit**: YES
  - Message: `chore(scaffold): final verification and cleanup`
  - Files: Any modified files from cleanup
  - Pre-commit: `dotnet build Hex.Scaffold.sln && dotnet test`

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.
>
> **Do NOT auto-proceed after verification. Wait for user's explicit approval before marking work complete.**

- [x] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, check endpoint, run command). For each "Must NOT Have": search codebase for forbidden patterns — reject with file:line if found. Check evidence files exist in `.sisyphus/evidence/`. Compare deliverables against plan. Verify the hexagonal dependency rules table matches actual `.csproj` references.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [x] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build Hex.Scaffold.sln` with zero warnings. Review all files for: `#pragma warning disable`, empty catches, `Console.Write` in prod, commented-out code, unused usings. Check AI slop: excessive comments, over-abstraction, generic names (data/result/item/temp). Verify 2-space indentation, Allman braces, file-scoped namespaces per `.editorconfig`. Verify all entities have `private set`. Verify all handlers return `ValueTask`. Load skill `dotnet-clean-arch` and run Phase 5 verification checklist.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [x] F3. **Real Manual QA** — `unspecified-high`
  Start from clean state. Execute EVERY QA scenario from EVERY task — follow exact steps, capture evidence. Test cross-task integration (full Create → cache → Kafka → MongoDB flow). Test edge cases: empty state, invalid input, missing optional dependencies (Redis down = cache miss, not crash). Save to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", verify actual implementation matches 1:1. Verify nothing beyond spec was built (no extra entities, no auth beyond placeholder, no K8s manifests). Check "Must NOT Have" compliance. Detect unaccounted changes. Verify all SDKs from the mandatory list are referenced in `.csproj` files. Verify all SDKs from the optional list (minus Quartz.NET) are referenced.
  Output: `Tasks [N/N compliant] | SDKs [N/N present] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

| After Task | Commit Message | Pre-commit Verification |
|-----------|----------------|------------------------|
| 1 | `chore(scaffold): initialize solution with hexagonal project structure` | `dotnet build` |
| 2 | `feat(domain): add base types and port interfaces` | `dotnet build` |
| 3 | `feat(domain): add Sample aggregate with VOs, events, and specifications` | `dotnet build` |
| 4 | `feat(application): add CQRS commands, queries, and handlers for Sample` | `dotnet build` |
| 5 | `feat(persistence): add PostgreSQL adapter with EF Core and Dapper` | `dotnet build` |
| 6 | `feat(persistence): add MongoDB adapter with read model repository` | `dotnet build` |
| 7 | `feat(persistence): add Redis cache adapter` | `dotnet build` |
| 8 | `feat(outbound): add Kafka producer and resilient HTTP client adapters` | `dotnet build` |
| 9 | `feat(inbound): add FastEndpoints CRUD for Sample` | `dotnet build` |
| 10 | `feat(inbound): add Kafka consumer for Sample events` | `dotnet build` |
| 11 | `feat(api): wire composition root with DI and middleware` | `dotnet build` |
| 12 | `feat(api): add observability, health checks, rate limiting, and OpenAPI` | `dotnet build` |
| 13 | `feat(docker): add multi-stage Dockerfile for AKS` | `docker build -t hex-scaffold .` |
| 14 | `test(arch): add NetArchTest hexagonal dependency rules` | `dotnet test --filter "Category=Architecture"` |
| 15 | `test(unit): add domain and application unit tests` | `dotnet test --filter "Category=Unit"` |
| 16 | `test(integration): add TestContainers integration tests` | `dotnet test --filter "Category=Integration"` |
| 17 | `chore(scaffold): final verification and appsettings cleanup` | `dotnet build && dotnet test` |

---

## Success Criteria

### Verification Commands
```bash
dotnet build Hex.Scaffold.sln                          # Expected: Build succeeded. 0 Warning(s). 0 Error(s)
dotnet test --filter "Category=Architecture"            # Expected: All tests passed
dotnet test --filter "Category=Unit"                    # Expected: All tests passed
dotnet test --filter "Category=Integration"             # Expected: All tests passed (requires Docker)
docker build -t hex-scaffold .                          # Expected: Successfully built, image < 200MB
```

### Final Checklist
- [ ] All "Must Have" items present and verified
- [ ] All "Must NOT Have" items absent (zero violations)
- [ ] All 17 tasks produce green `dotnet build`
- [ ] Hexagonal dependency rules pass NetArchTest
- [ ] Sample entity demonstrates full data flow (REST → PostgreSQL → Kafka → MongoDB, with Redis caching)
- [ ] Dockerfile builds successfully
- [ ] OpenAPI accessible at `/swagger`
- [ ] Health checks at `/healthz` and `/ready`
