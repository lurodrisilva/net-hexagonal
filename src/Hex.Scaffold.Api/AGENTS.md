<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Api

## Purpose
The composition root and ASP.NET Core host. Wires DI, registers FastEndpoints, configures observability, health checks, rate limiting, JSON serialization, and the feature selector. References every other project; nothing else references this one.

## Key Files
| File | Description |
|------|-------------|
| `Program.cs` | App entry point — calls into `Configurations/*` extension methods and runs the host |
| `appsettings.json` | Default config (local dev) |
| `appsettings.Development.json` | Dev overrides |
| `appsettings.Testing.json` | Test overrides used by integration tests |
| `GlobalUsings.cs` | Project-wide usings |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Configurations/` | Composition extension methods: `ServiceConfigs`, `MiddlewareConfig`, `ObservabilityConfig`, `HealthCheckConfig`, etc. |
| `Options/` | Strongly-typed options classes (`FeaturesOptions`, `RateLimitOptions`, etc.) bound from configuration |

## For AI Agents

### Working In This Directory
- `FeaturesOptions` (in `Options/`) selects the active adapters at startup: `InboundAdapter=rest|kafka`, `OutboundAdapter=rest|kafka`, `Persistence=postgres|mongo`, `UseRedis=bool`. `ServiceConfigs.cs` reads it and registers only the matching adapters.
- Health checks (`HealthCheckConfig.cs`) are gated on the same `FeaturesOptions` — probing a backend the app never talks to defaults to localhost and times out, breaking readiness.
- Snake_case JSON is set globally on the FastEndpoints serializer; do not add per-property `[JsonPropertyName]`.
- The Postgres health check currently opens an unpooled `NpgsqlConnection` per probe — there's an open issue to switch it to the pooled `NpgsqlDataSource` singleton to avoid handshake overhead under load.
- Observability (`ObservabilityConfig.cs`): OTel TracerProvider subscribes to `Npgsql` (Postgres spans) and `KafkaTelemetry.SourceName` (Kafka spans). Without those `AddSource(...)` calls, App Insights' Application Map shows no DB / Kafka edges. Azure Monitor exporters are wired only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.

### Testing Requirements
- Integration tests use `WebApplicationFactory<Program>` with an `appsettings.Testing.json` override; Testcontainers spins up Postgres/Redis.

### Common Patterns
- Adapter implementations matching `*Service|*Repository|*Publisher|*Client` are auto-registered by Scrutor with `RegistrationStrategy.Skip` — explicit registrations win.
- Each `Configurations/*` extension is a pure DI helper that takes `IServiceCollection` and returns it; no logic in `Program.cs` itself.

## Dependencies

### Internal
- All other `Hex.Scaffold.*` projects (composition root).

### External
- `FastEndpoints`, `FastEndpoints.Swagger`
- `OpenTelemetry.*`, `Azure.Monitor.OpenTelemetry.Exporter`
- `Microsoft.Extensions.Diagnostics.HealthChecks`
- `Scrutor` (assembly scanning for adapter auto-registration)

<!-- MANUAL: -->
