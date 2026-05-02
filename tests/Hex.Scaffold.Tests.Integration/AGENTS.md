<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Tests.Integration

## Purpose
End-to-end integration tests that boot the API in-process via `WebApplicationFactory<Program>` and exercise it against real Postgres and Redis containers spun up by Testcontainers. Validates the full HTTP → handler → EF → DB round trip.

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Fixtures/` | Shared test fixtures: `WebAppFactory`, container-spinup helpers, `appsettings.Testing.json` overrides |

## For AI Agents

### Working In This Directory
- **Docker must be running** — Testcontainers fails fast if it can't reach the daemon.
- The `WebAppFactory` fixture is `IClassFixture` per collection so containers spin up once and are shared across tests in the same collection. Keep test classes in the same collection if they share data setup costs.
- Tests use the real DI graph and the real Mediator pipeline — they're not mocking-friendly. If you need to mock, use unit tests.
- The override config (`appsettings.Testing.json`) points at the Testcontainers-managed connection strings via env vars set in the fixture.

### Testing Requirements
- Run on every PR. CI must have Docker available.
- A single test: `dotnet test tests/Hex.Scaffold.Tests.Integration --filter "FullyQualifiedName~ClassName.MethodName"`.
- Local re-runs: containers persist between runs by default (Testcontainers reuses by `--label`); use `docker ps` to inspect, `docker rm -f` to reset.

### Common Patterns
- One test class per endpoint or per use-case slice.
- Use the factory's `CreateClient()` to make HTTP calls; deserialize responses with the same `JsonSerializerOptions` (snake_case) the API uses.
- Reset the database between tests with `Respawn` or by transaction-scoping each test (project picks one — check the fixture).

## Dependencies

### Internal
- Tests the full `Hex.Scaffold.*` graph end-to-end.

### External
- `Microsoft.AspNetCore.Mvc.Testing`
- `Testcontainers`, `Testcontainers.PostgreSql`, `Testcontainers.Redis`
- `xunit`, `Shouldly`

<!-- MANUAL: -->
