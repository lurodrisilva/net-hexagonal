<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Application

## Purpose
Use-case layer: CQRS commands/queries, their handlers, mediator pipeline behaviors, and application-level types like `PagedResult<T>` and `Constants`. Depends only on Domain. No infrastructure concerns.

## Key Files
| File | Description |
|------|-------------|
| `Constants.cs` | Application-wide constants (e.g. cache keys, limits) |
| `PagedResult.cs` | Generic paginated response envelope (used by list queries) |
| `GlobalUsings.cs` | Imports `Mediator`, Domain `Common`, etc. |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Accounts/` | One folder per use case: `Create/`, `Get/`, `List/`, `Update/`. Each contains the command/query, handler, and any operation-specific port (e.g. `IListAccountsQueryService`) |
| `Behaviors/` | Mediator pipeline behaviors (logging, validation, etc.) |

## For AI Agents

### Working In This Directory
- A new operation goes in `Accounts/<Operation>/` with: `<Operation>Command.cs` (or `Query`), `<Operation>Handler.cs`, optionally `<Operation>Validator.cs`. Mediator's source generator picks them up automatically.
- Handlers implement `ICommandHandler<TCmd, TResponse>` or `IQueryHandler<TQuery, TResponse>` and return `Result<T>`.
- Query-specific ports (e.g. `IListAccountsQueryService` for cursor pagination) live next to the query they serve, not in Domain. Implementations live in Persistence.
- Do not call Domain entities' setters from here — call domain methods that enforce invariants (`Account.Create`, `Account.ApplyUpdate`).

### Testing Requirements
- Handler tests in `tests/Hex.Scaffold.Tests.Unit/Application` mock the repository ports with NSubstitute and assert against the returned `Result<T>`.

### Common Patterns
- Command/query immutable records.
- Inbound DTOs (in Adapters.Inbound) get translated to Application commands at the endpoint boundary; Application never sees `JsonElement` — it sees `Maybe<T>` / `(bool HasValue, T? Value)` tuples.

## Dependencies

### Internal
- `Hex.Scaffold.Domain` — only allowed reference.

### External
- `Mediator.SourceGenerator` (build-time only)
- `FluentValidation` (consumed via FastEndpoints in the inbound layer; the validator base lives there)

<!-- MANUAL: -->
