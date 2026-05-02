<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# tests

## Purpose
Four test projects: architecture (build-time fitness functions), unit, integration (real backends via Testcontainers), and load (k6 against a deployed cluster). Architecture tests must pass for any PR.

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Hex.Scaffold.Tests.Architecture/` | NetArchTest fitness functions enforcing the hexagonal dep rules (see `Hex.Scaffold.Tests.Architecture/AGENTS.md`) |
| `Hex.Scaffold.Tests.Unit/` | xUnit unit tests for Domain and Application layers (see `Hex.Scaffold.Tests.Unit/AGENTS.md`) |
| `Hex.Scaffold.Tests.Integration/` | End-to-end tests using `WebApplicationFactory` + Testcontainers (see `Hex.Scaffold.Tests.Integration/AGENTS.md`) |
| `loadtest/` | k6-Operator REST load tests + a Strimzi Kafka producer job (see `loadtest/AGENTS.md`) |

## For AI Agents

### Working In This Directory
- Run the architecture tests before any PR that adds a project reference: `dotnet test --filter "Category=Architecture"`. They fail the build if Domain references infrastructure or adapters reference each other.
- `dotnet test` runs everything; integration tests need Docker for Testcontainers.
- A single test: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`.

### Testing Requirements
- Add a unit test for every new domain method or handler. Add an integration test for every new endpoint.
- Architecture tests are not optional — if your change requires loosening one, the change is wrong.

### Common Patterns
- xUnit + Shouldly for assertions, NSubstitute for mocks.
- Integration tests inherit from a shared `WebAppFactory` fixture that spins up Postgres + Redis containers once per test run.

## Dependencies

### External
- `xunit`, `Shouldly`, `NSubstitute`
- `NetArchTest.Rules`
- `Testcontainers`, `Testcontainers.PostgreSql`, `Testcontainers.Redis`
- `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`)
- k6 (load tests; runs in-cluster via the k6-operator)

<!-- MANUAL: -->
