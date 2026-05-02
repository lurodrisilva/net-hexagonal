<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Adapters.Inbound

## Purpose
Driving adapters — the entry points by which the outside world calls into Application. Two transports today: HTTP via FastEndpoints (`Api/`) and a Kafka consumer (`Messaging/`). Depends on Domain and Application; never on other adapter projects.

## Key Files
| File | Description |
|------|-------------|
| `GlobalUsings.cs` | Imports for `FastEndpoints`, `Mediator`, FluentValidation, etc. |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Api/Accounts/` | One file per endpoint: `CreateAccount.cs`, `GetAccount.cs`, `UpdateAccount.cs`, `ListAccounts.cs`, plus `AccountFieldHelpers.cs` for partial-update plumbing |
| `Api/Extensions/` | `ResultExtensions` — maps `Result<T>` onto HTTP `Results<Ok<T>, NotFound, ProblemHttpResult>` |
| `Messaging/` | `AccountEventConsumer.cs` — Confluent.Kafka `BackgroundService` that consumes account events and dispatches commands |

## For AI Agents

### Working In This Directory
- Endpoints are FastEndpoints classes (not controllers). Each defines `Configure()` (route + summary + tags) and `ExecuteAsync()`. Validators are FluentValidation `Validator<T>` subclasses in the same file.
- For partial updates (Stripe POST semantics), request fields are `JsonElement`. `Undefined` = key omitted (leave alone), `Null` = clear, value = set. The `AccountFieldHelpers.ToMaybe*` extensions collapse this into `(bool HasValue, T? Value)` for the Application command.
- `AllowAnonymous()` is on every endpoint today — auth lives outside scope of this scaffold.
- Stripe routes do **not** use `201 Created` — Stripe returns `200 OK` with the resource body. Match that.
- `acct_`-prefixed IDs are validated in the request validator (`Must(id => id is null || id.StartsWith("acct_", …))`), not in Domain.

### Testing Requirements
- Integration tests under `tests/Hex.Scaffold.Tests.Integration` exercise endpoints end-to-end with Testcontainers.

### Common Patterns
- Route names mirror Stripe v2: `/v2/core/accounts`, `/v2/core/accounts/{id}`.
- All HTTP responses serialize via `JsonNamingPolicy.SnakeCaseLower` (configured at composition root). Don't add `[JsonPropertyName]` attributes.
- Custom Activity tracing for Kafka comes from `KafkaTelemetry.Source` (Domain/Common). The consumer wraps message handling in `StartActivity("kafka consume", …)`.

## Dependencies

### Internal
- `Hex.Scaffold.Domain` (value objects, IDs)
- `Hex.Scaffold.Application` (commands, queries, DTOs)

### External
- `FastEndpoints` (HTTP)
- `FluentValidation` via FastEndpoints
- `Confluent.Kafka` (consumer)

<!-- MANUAL: -->
