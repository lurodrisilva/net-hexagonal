# Hex.Scaffold

A production-grade **.NET 10 hexagonal (ports & adapters) microservice scaffold**. It demonstrates a canonical architecture for a cloud-native service with CQRS, domain events, strongly-typed IDs, and pluggable persistence, messaging, and HTTP adapters.

This template is intended as a starting point for Azure AKS / Kubernetes workloads but nothing outside the composition root is cloud-specific.

---

## Highlights

- **Hexagonal architecture** with architecture tests enforcing layer boundaries (NetArchTest).
- **CQRS via Mediator** (source generator) — no reflection, no runtime handler scanning.
- **Domain events** dispatched through EF Core `SaveChangesInterceptor`, bridged to Kafka via `INotificationHandler`.
- **Strongly-typed value objects** with [Vogen](https://github.com/SteveDunn/Vogen) for IDs and primitives.
- **Result pattern** for explicit outcome handling; no exceptions for expected flows.
- **FastEndpoints** API (not controllers) with FluentValidation, OpenAPI, and Scalar UI.
- **Polyglot persistence** out of the box: PostgreSQL (writes), Dapper (reads), MongoDB (denormalised read models), Redis (cache).
- **Kafka** producer and consumer (BackgroundService).
- **Resilient HTTP client** via `Microsoft.Extensions.Http.Resilience`.
- **Full observability** — OpenTelemetry traces, metrics, logs (OTLP) + Serilog.
- **Health checks** at `/healthz` (liveness) and `/ready` (readiness).
- **Rate limiting** (per-IP fixed window).
- **Three test tiers**: unit (xUnit + NSubstitute + Shouldly), integration (Testcontainers), architecture (NetArchTest).

---

## Quick Start

```bash
# build everything
dotnet build

# run the API (listens on :8080)
dotnet run --project src/Hex.Scaffold.Api

# run all tests
dotnet test

# run only architecture tests (fast, no Docker)
dotnet test --filter "Category=Architecture"
```

Integration tests require Docker (Testcontainers spin up PostgreSQL and Redis automatically).

Once the API is running:

- Scalar UI: `http://localhost:8080/scalar/v1`
- Swagger JSON: `http://localhost:8080/swagger/v1/swagger.json`
- Liveness: `http://localhost:8080/healthz`
- Readiness: `http://localhost:8080/ready`

### EF Core migrations

```bash
dotnet ef migrations add <Name> \
  --project src/Hex.Scaffold.Adapters.Persistence \
  --startup-project src/Hex.Scaffold.Api
```

---

## Architecture at a glance

```mermaid
flowchart LR
    subgraph Inbound["Inbound adapters"]
        HTTP[FastEndpoints HTTP]
        KC[Kafka Consumer]
    end

    subgraph Core["Application core"]
        APP[Application<br/>CQRS handlers]
        DOM[Domain<br/>aggregates · events · ports]
    end

    subgraph Outbound["Outbound adapters"]
        EF[EF Core / PostgreSQL]
        DAP[Dapper / PostgreSQL]
        MON[MongoDB read model]
        RED[Redis cache]
        KP[Kafka producer]
        HX[Resilient HTTP client]
    end

    HTTP --> APP
    KC --> MON
    APP --> DOM
    APP -.uses ports.-> EF
    APP -.uses ports.-> DAP
    APP -.uses ports.-> RED
    APP -.uses ports.-> HX
    DOM -.domain events.-> KP
```

Dependency rule (enforced by [`HexagonalDependencyTests`](tests/Hex.Scaffold.Tests.Architecture/HexagonalDependencyTests.cs)):

```
Domain  ←  Application  ←  Adapters.*  ←  Api (composition root)
```

See [`docs/architecture.md`](docs/architecture.md) for the full breakdown.

---

## Documentation

| Doc | What's in it |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Hexagonal structure, dependency flow, project map, architecture tests |
| [`docs/domain.md`](docs/domain.md) | Aggregates, value objects, domain events, Result pattern, specifications, SmartEnum |
| [`docs/application.md`](docs/application.md) | CQRS use cases, Mediator pipeline, logging behavior, ports |
| [`docs/adapters.md`](docs/adapters.md) | Inbound (HTTP, Kafka consumer) and outbound (EF, Dapper, Mongo, Redis, Kafka, HTTP) adapters |
| [`docs/api.md`](docs/api.md) | HTTP endpoints, request/response schemas, error mapping |
| [`docs/events.md`](docs/events.md) | Domain event dispatch, Kafka publish, read-model projection flow |
| [`docs/observability.md`](docs/observability.md) | OpenTelemetry, Serilog, health checks, rate limiting |
| [`docs/testing.md`](docs/testing.md) | Unit, integration, architecture test strategy |
| [`docs/development.md`](docs/development.md) | Local infra (Docker), configuration, EF migrations, troubleshooting |

---

## Solution layout

```
src/
  Hex.Scaffold.Domain/                 # Aggregates, value objects, domain events, ports (interfaces)
  Hex.Scaffold.Application/            # CQRS commands/queries/handlers, DTOs, pipeline behaviors
  Hex.Scaffold.Adapters.Inbound/       # FastEndpoints HTTP + Kafka BackgroundService consumer
  Hex.Scaffold.Adapters.Outbound/      # Kafka producer, resilient HTTP client
  Hex.Scaffold.Adapters.Persistence/   # EF Core (Postgres), Dapper queries, MongoDB, Redis
  Hex.Scaffold.Api/                    # Composition root — DI, OTel, middleware, health, rate limiting
tests/
  Hex.Scaffold.Tests.Architecture/     # NetArchTest — enforces dependency rules
  Hex.Scaffold.Tests.Unit/             # xUnit + NSubstitute + Shouldly
  Hex.Scaffold.Tests.Integration/      # Testcontainers (PostgreSQL, Redis) + WebApplicationFactory
```

---

## Tech stack

.NET 10 · C# latest · FastEndpoints · Mediator (source generator) · Vogen · EF Core 10 (Npgsql) · Dapper · MongoDB.Driver · StackExchange.Redis · Confluent.Kafka · Microsoft.Extensions.Http.Resilience · OpenTelemetry · Serilog · FluentValidation · Scrutor · NetArchTest · xUnit · Shouldly · NSubstitute · Testcontainers.

Package versions are managed centrally in [`Directory.Packages.props`](Directory.Packages.props).

---

## License

See repository metadata.
