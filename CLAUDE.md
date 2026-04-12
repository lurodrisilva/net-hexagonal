# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Required Skill

**Always read and follow `.opencode/skills/dotnet-clean-arch.md` before implementing any feature, aggregate, use case, endpoint, or domain change.** That file contains the authoritative patterns, phase-by-phase implementation guides, coding style rules, anti-patterns, and verification checklists for this codebase. It is non-negotiable.

## Build & Run

```bash
dotnet build                                          # build entire solution
dotnet run --project src/Hex.Scaffold.Api             # run the API (listens on port 8080)
dotnet test                                           # run all tests
dotnet test --filter "Category=Architecture"          # architecture tests only
dotnet test tests/Hex.Scaffold.Tests.Unit             # unit tests only
dotnet test tests/Hex.Scaffold.Tests.Integration      # integration tests (requires Docker for Testcontainers)
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"  # single test
```

EF Core migrations (from repo root):
```bash
dotnet ef migrations add <Name> --project src/Hex.Scaffold.Adapters.Persistence --startup-project src/Hex.Scaffold.Api
```

## Architecture

Hexagonal (ports & adapters) .NET 10 microservice scaffold. Architecture tests in `tests/Hex.Scaffold.Tests.Architecture/HexagonalDependencyTests.cs` enforce dependency rules at build time via NetArchTest — these **must pass** for any PR.

### Dependency flow (strictly enforced)

```
Domain  <--  Application  <--  Adapters.Inbound
                               Adapters.Outbound
                               Adapters.Persistence
                                      |
                               Api (composition root)
```

- **Domain** depends on nothing (only Mediator.Abstractions, Vogen, logging abstractions).
- **Application** depends only on Domain.
- **Adapter projects** depend on Domain (and Persistence also on Application for query services), never on each other.
- **Api** is the composition root — references all projects, wires DI via `Configurations/ServiceConfigs.cs`.

### Key patterns

- **CQRS via Mediator source generator** — commands/queries in `Application/Samples/{Operation}/`, handlers implement `ICommandHandler<,>` or `IQueryHandler<,>`. Mediator is source-generated (not reflection-based MediatR).
- **Result pattern** — handlers return `Result` or `Result<T>` (defined in `Domain/Common/Result.cs`). Endpoints map results to HTTP via extension methods in `Adapters.Inbound/Api/Extensions/ResultExtensions.cs`.
- **Value objects via Vogen** — strongly-typed IDs and domain primitives (`SampleId`, `SampleName`) use `[ValueObject<T>]` with built-in validation.
- **SmartEnum** — custom implementation in `Domain/Common/SmartEnum.cs` for enumeration classes (e.g., `SampleStatus`).
- **Specification pattern** — `Domain/Common/Specification.cs` for composable query predicates.
- **Domain events** — entities extend `HasDomainEventsBase`, register events via `RegisterDomainEvent()`. Events are dispatched through `IDomainEventDispatcher` (implemented by `MediatorDomainEventDispatcher` in Persistence).
- **FastEndpoints** — API endpoints (not controllers). Each endpoint is a class in `Adapters.Inbound/Api/Samples/` with `Configure()` + `ExecuteAsync()`. Validators use FluentValidation via FastEndpoints' `Validator<T>`.
- **Scrutor** for auto-registration — adapter implementations matching `*Service`, `*Repository`, `*Publisher`, `*Client` suffix are auto-registered. Explicit registrations take precedence (`RegistrationStrategy.Skip`).

### Ports (interfaces in Domain)

Outbound ports live in `Domain/Ports/Outbound/`: `IRepository<T>`, `IReadRepository<T>`, `IEventPublisher`, `ICacheService`, `IExternalApiClient`, `ISampleReadModelRepository`. Application can also define query-specific ports (e.g., `IListSamplesQueryService`).

### Adapter implementations

- **Persistence**: EF Core (PostgreSQL) for writes, Dapper for reads, MongoDB for read models, Redis for caching.
- **Outbound**: Kafka producer, resilient HTTP client (via `Microsoft.Extensions.Http.Resilience`).
- **Inbound**: FastEndpoints (HTTP), Kafka consumer (`BackgroundService`).

## Conventions

- .NET 10, C# latest, nullable enabled, warnings-as-errors.
- Central package management via `Directory.Packages.props` — add version there, reference without version in `.csproj`.
- Global usings per project (e.g., Domain imports `Hex.Scaffold.Domain.Common`, `Mediator`, `Microsoft.Extensions.Logging`).
- Solution file is XML-based `.slnx` format (`Hex.Scaffold.slnx`).
- Integration tests use Testcontainers (PostgreSQL, Redis) — Docker must be running.
- Unit tests use xUnit + NSubstitute + Shouldly.
- Aggregate entities have private setters (enforced by architecture tests).
