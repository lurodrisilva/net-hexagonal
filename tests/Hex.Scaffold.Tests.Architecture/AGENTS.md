<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Tests.Architecture

## Purpose
Build-time fitness functions enforcing the hexagonal dependency rules. Uses NetArchTest to walk the assembly graph and assert direction and absence of forbidden references. **These tests must pass for any PR** — the project is referenced by the build pipeline.

## Key Files
| File | Description |
|------|-------------|
| `HexagonalDependencyTests.cs` | Asserts: Domain has no references; Application references only Domain; adapters reference Domain (Persistence may also reference Application for query services); adapters do not reference each other |

## For AI Agents

### Working In This Directory
- Tests are tagged `[Trait("Category", "Architecture")]` so they can be filtered: `dotnet test --filter "Category=Architecture"`.
- If you intentionally need to relax a rule (rare), update the test in the same PR with a comment explaining why — never silently skip.
- The aggregate's private-setter rule is also asserted here: domain entities cannot have public setters.

### Testing Requirements
- The architecture project has no Testcontainers dependency — runs in milliseconds. CI runs it on every PR.

### Common Patterns
- Each rule is a single test method using `Types.InAssembly(typeof(Marker).Assembly).That()…ShouldNot()…GetResult()`.
- Failure message includes the offending type list, so debugging is direct.

## Dependencies

### Internal
- References every `Hex.Scaffold.*` assembly to inspect them.

### External
- `NetArchTest.Rules`
- `xunit`

<!-- MANUAL: -->
