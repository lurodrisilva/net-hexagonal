<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Tests.Unit

## Purpose
Pure unit tests for the Domain and Application layers. No infrastructure, no I/O — handlers run with mocked ports.

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Domain/` | Tests for `Account` aggregate behavior, value objects, specifications |
| `Application/` | Tests for command/query handlers (one test class per handler) |

## For AI Agents

### Working In This Directory
- Mock Domain ports (`IRepository<T>`, `IEventPublisher`, etc.) with NSubstitute. Don't mock Application services from inside Application tests.
- Assert against `Result<T>` outcomes (`result.IsSuccess`, `result.Errors`, `result.Status`) — never assert on exceptions for expected failures.
- Tests should be fast (<100ms each); flagged as `[Fact]` or `[Theory]`. No async file/network I/O.

### Testing Requirements
- Add a test for every domain method change and every handler change. CI runs the full unit suite on every push.
- Run a single test: `dotnet test tests/Hex.Scaffold.Tests.Unit --filter "FullyQualifiedName~ClassName.MethodName"`.

### Common Patterns
- Arrange/Act/Assert separated by blank lines.
- Builder helpers for aggregate construction live next to the tests that need them — don't create a test-fixtures god-class.
- Use `Shouldly` for fluent assertions (`result.ShouldBeTrue()`, `errors.ShouldContain(...)`).

## Dependencies

### Internal
- `Hex.Scaffold.Domain`
- `Hex.Scaffold.Application`

### External
- `xunit`, `Shouldly`, `NSubstitute`

<!-- MANUAL: -->
