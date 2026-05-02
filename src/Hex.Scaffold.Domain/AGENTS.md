<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Domain

## Purpose
The pure domain layer. Contains aggregates, value objects, domain events, the `Result<T>` pattern, the `Specification<T>` base, and outbound port interfaces (the contracts adapters implement). Depends only on Mediator.Abstractions, Vogen, and `Microsoft.Extensions.Logging`. No EF, no HTTP, no infrastructure.

## Key Files
| File | Description |
|------|-------------|
| `GlobalUsings.cs` | Imports `Hex.Scaffold.Domain.Common`, `Mediator`, `Microsoft.Extensions.Logging` for every file in this project |
| `Hex.Scaffold.Domain.csproj` | Project file — must have NO project references |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `AccountAggregate/` | The `Account` aggregate (Stripe v2 reproduction): root entity, value objects, events, handlers, specifications |
| `Common/` | `Result<T>`, `Specification<T>`, `HasDomainEventsBase`, `KafkaTelemetry` (shared `ActivitySource`) |
| `Ports/Outbound/` | Interfaces adapters implement: `IRepository<T>`, `IReadRepository<T>`, `IEventPublisher`, `ICacheService`, `IExternalApiClient` |

## For AI Agents

### Working In This Directory
- `Account.Create()` generates the `acct_`-prefixed `AccountId` itself before EF's `IdentityMap.Add` ever sees the entity (see PR #20 history for why this ordering matters).
- Nested Stripe-shaped objects (`configuration`, `identity`, `defaults`, `requirements`, `future_requirements`, `metadata`) are stored as raw JSON strings on the aggregate — Persistence maps them to `jsonb` columns.
- Partial-update semantics use `(bool HasValue, T? Value)` tuples produced by the inbound layer; the aggregate's `ApplyUpdate` reads only fields where `HasValue` is true (Stripe convention: absent keys are left alone).
- Register domain events via `RegisterDomainEvent()` — they're dispatched by `MediatorDomainEventDispatcher` in Persistence after `SaveChangesAsync`.

### Testing Requirements
- Unit tests in `tests/Hex.Scaffold.Tests.Unit/Domain` use xUnit + Shouldly + NSubstitute.
- The architecture test `HexagonalDependencyTests` verifies Domain has no forbidden references.

### Common Patterns
- Strongly-typed IDs via Vogen `[ValueObject<T>]` with built-in validation.
- Result pattern: handlers return `Result` or `Result<T>`, never throw for expected failures.
- Specification pattern in `Common/Specification.cs` for composable query predicates (`AccountByIdSpec`, etc.).

## Dependencies

### Internal
None. This is the bottom of the dependency graph.

### External
- `Mediator.Abstractions` — for `INotification` (domain events)
- `Vogen` — value objects via source generator
- `Microsoft.Extensions.Logging.Abstractions`

<!-- MANUAL: -->
