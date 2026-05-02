<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# net-hexagonal (Hex.Scaffold)

## Purpose
.NET 10 microservice scaffold demonstrating Hexagonal (Ports & Adapters) architecture. The exposed aggregate is `Account` — a Stripe v2 `v2.core.account` reproduction reachable through the four endpoints `POST/GET /v2/core/accounts` and `POST/GET /v2/core/accounts/{id}`. Optional Kafka inbound/outbound, Postgres/Mongo persistence, and Redis cache layers are switched on at the composition root via `FeaturesOptions`.

## Key Files
| File | Description |
|------|-------------|
| `Hex.Scaffold.slnx` | Solution file (XML format, replaces .sln) |
| `Directory.Packages.props` | Central package management — every NuGet version lives here |
| `Directory.Build.props` | Solution-wide build settings (nullable, warnings-as-errors, .NET 10) |
| `global.json` | Pins the .NET SDK version |
| `Dockerfile` | Multi-stage build for the API image |
| `Makefile` | Convenience wrapper around `dotnet` and Docker commands |
| `CLAUDE.md` | Authoritative coding guide for AI agents — read before any change |
| `README.md` | Public project README |
| `request.sh` | Smoke-test script: one curl per route + verb |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `src/` | All production code (see `src/AGENTS.md`) |
| `tests/` | Unit, integration, architecture, and load tests (see `tests/AGENTS.md`) |
| `deploy/` | Helm chart for AKS deployment (see `deploy/AGENTS.md`) |
| `docs/` | Long-form architecture and ops docs (see `docs/AGENTS.md`) |

## Deployment Target (current)

**AKS cluster:** `aks-test` in `aks-test-rg` (brazilsouth, K8s 1.34, Free tier, kubenet, RBAC disabled, public API)

**Subscription:** `lcamargoreis-microsoft-subscription` (`df21ed78-be77-40e3-9184-38eb23175791`)

**Database:** `pgsql-pp-platinum-1` (PG Flexible Server) in `resources-test-rg`

**Note:** Seven PG Flex servers exist in `resources-test-rg` forming a tier ladder (bronze/silver/gold/platinum); only `platinum-1` is the load-test target.

For deployment details, see `deploy/AGENTS.md`. For load-test environment and current bottleneck analysis, see `tests/loadtest/AGENTS.md`.

## For AI Agents

### Working In This Directory
- **Always read `CLAUDE.md` and `.opencode/skills/dotnet-clean-arch.md` before changing any aggregate, use case, endpoint, or domain code.** Those files are authoritative.
- Add NuGet versions only in `Directory.Packages.props` — `.csproj` files reference packages without versions.
- Wire format is `snake_case` end-to-end (set at composition root via `JsonNamingPolicy.SnakeCaseLower`); do not add `[JsonPropertyName]` attributes.
- Architecture tests in `tests/Hex.Scaffold.Tests.Architecture` enforce the hexagonal dependency rules at build time. Any PR that violates them fails CI.

### Testing Requirements
- `dotnet test` runs everything. Integration tests need Docker for Testcontainers (Postgres, Redis).
- `dotnet test --filter "Category=Architecture"` is the minimum smoke test before opening a PR that touches project references.

### Common Patterns
- CQRS with the Mediator source generator (not MediatR), commands/queries under `Application/<Aggregate>/<Operation>/`.
- Result pattern (`Result<T>`) instead of throwing for expected failures.
- Strongly-typed IDs with Vogen (`AccountId` is a string-typed value object with `acct_` prefix).
- FastEndpoints for HTTP, not controllers.

## Dependencies

### External
- .NET 10 SDK
- Docker (for Testcontainers + container builds)
- PostgreSQL 16, MongoDB 7, Redis 7, Kafka (optional, feature-gated)
- Vogen (value objects), Mediator (source generator), FastEndpoints, Scrutor (auto-registration), Vogen, EF Core 10, Npgsql

<!-- MANUAL: -->
