<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# src

## Purpose
Production code organized by hexagonal layer. The dependency rule (enforced at build time by `tests/Hex.Scaffold.Tests.Architecture`) is strict and one-way:

```
Domain  <--  Application  <--  Adapters.Inbound
                              Adapters.Outbound
                              Adapters.Persistence
                                     |
                              Api (composition root)
```

Domain depends on nothing. Application depends only on Domain. Adapter projects depend on Domain (Persistence also on Application for query services), never on each other. Api references everything and wires DI.

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Hex.Scaffold.Domain/` | Aggregates, value objects, domain events, ports (see `Hex.Scaffold.Domain/AGENTS.md`) |
| `Hex.Scaffold.Application/` | CQRS commands/queries, handlers, behaviors (see `Hex.Scaffold.Application/AGENTS.md`) |
| `Hex.Scaffold.Adapters.Inbound/` | HTTP (FastEndpoints) and Kafka consumer (see `Hex.Scaffold.Adapters.Inbound/AGENTS.md`) |
| `Hex.Scaffold.Adapters.Outbound/` | Kafka producer, resilient HTTP client (see `Hex.Scaffold.Adapters.Outbound/AGENTS.md`) |
| `Hex.Scaffold.Adapters.Persistence/` | EF Core (Postgres), MongoDB, Redis (see `Hex.Scaffold.Adapters.Persistence/AGENTS.md`) |
| `Hex.Scaffold.Api/` | Composition root + ASP.NET Core host (see `Hex.Scaffold.Api/AGENTS.md`) |

## For AI Agents

### Working In This Directory
- Adding a new aggregate: create the type in Domain, then commands/queries in Application, then a persistence config in Persistence/PostgreSql/Config, then endpoints in Adapters.Inbound/Api/<Aggregate>. Wire it up in `Api/Configurations/ServiceConfigs.cs`.
- Never add a project reference that crosses a layer boundary in the wrong direction. The architecture test will fail the build.
- Adapter implementations matching `*Service`, `*Repository`, `*Publisher`, `*Client` are auto-registered by Scrutor; explicit registrations win (`RegistrationStrategy.Skip`).

### Testing Requirements
- Architecture tests must pass: `dotnet test --filter "Category=Architecture"`.
- Each aggregate gets unit tests in `tests/Hex.Scaffold.Tests.Unit/Domain` and handler tests in `tests/Hex.Scaffold.Tests.Unit/Application`.

### Common Patterns
- Global usings live in each project's `GlobalUsings.cs` — common namespaces (`Mediator`, `Microsoft.Extensions.Logging`, project's `Common` namespace) shouldn't be imported per-file.
- Aggregate entities have private setters (enforced by architecture tests).

## Dependencies

### Internal
- All cross-layer wiring happens in `Hex.Scaffold.Api/Configurations/ServiceConfigs.cs`.

### External
- See `Directory.Packages.props` at the repo root for the canonical version list.

<!-- MANUAL: -->
