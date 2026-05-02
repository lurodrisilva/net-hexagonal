<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# docs

## Purpose
Long-form project documentation: architecture rationale, per-layer guides, ops runbooks. These are human-readable companions to `CLAUDE.md` (which is the authoritative AI-agent guide) and the per-project `AGENTS.md` files.

## Key Files
| File | Description |
|------|-------------|
| `architecture.md` | Hexagonal layout overview, dependency rule, why Domain depends on nothing |
| `domain.md` | Aggregate design, value objects, domain events, Result pattern |
| `application.md` | CQRS conventions, handler shape, pipeline behaviors |
| `adapters.md` | Inbound/outbound/persistence adapter conventions |
| `api.md` | FastEndpoints conventions, Stripe v2 wire format, snake_case JSON |
| `database.md` | Postgres schema, jsonb columns, EF migrations, NpgsqlDataSource setup |
| `events.md` | Domain-event flow, `EventDispatcherInterceptor`, Kafka publishing |
| `observability.md` | OTel pipeline, App Insights wiring, Application Map dependency edges |
| `loadtest.md` | k6-operator workflow, threshold rationale, capacity math |
| `deployment.md` | Helm chart usage, AKS specifics, env-var precedence |
| `testing.md` | Unit / integration / architecture test strategy |
| `development.md` | Local dev setup, Docker Compose, EF migrations workflow |

## For AI Agents

### Working In This Directory
- These docs are **descriptive, not authoritative** for code changes. The authoritative sources are `CLAUDE.md` (root) and `.opencode/skills/dotnet-clean-arch.md`.
- Update the relevant doc when you change the corresponding layer. Stale docs here are worse than missing ones — readers trust them.
- Keep cross-references: `architecture.md` should link to `domain.md` / `application.md` / etc. when introducing those concepts.

### Runtime & Deployment Environment

The repo's runtime environment (cluster topology, deployed pod shape, PG Flex configuration, load-test bottleneck analysis) is documented inline in `tests/loadtest/AGENTS.md` and `deploy/AGENTS.md`. These are the authoritative sources for understanding load-test results and deployment state. New runtime knowledge discovered during diagnostic sessions should be added to those files, not to `docs/`.

### Testing Requirements
- No automated test coverage. Sanity-check Markdown locally.

### Common Patterns
- One layer / one cross-cutting concern per file.
- Code blocks use language fences (` ```csharp `, ` ```yaml `) for IDE highlighting.

## Dependencies

None — these are static markdown files.

<!-- MANUAL: -->
