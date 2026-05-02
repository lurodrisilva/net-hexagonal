<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# Hex.Scaffold.Adapters.Outbound

## Purpose
Driven adapters that talk to the outside world: a Kafka producer for domain-event publishing and a resilient `HttpClient` for outbound REST calls. Both implement ports defined in `Hex.Scaffold.Domain/Ports/Outbound`.

## Key Files
| File | Description |
|------|-------------|
| `GlobalUsings.cs` | Imports for `Confluent.Kafka`, polly resilience, etc. |

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `Http/` | `ExternalApiClient` — wraps `HttpClient` with `Microsoft.Extensions.Http.Resilience` retry / circuit-breaker policies |
| `Messaging/` | `KafkaEventPublisher` — implements `IEventPublisher`, emits OTel spans via `KafkaTelemetry.Source` |

## For AI Agents

### Working In This Directory
- This project must NOT reference `Hex.Scaffold.Application`. It implements only Domain ports.
- `KafkaEventPublisher.PublishAsync` wraps each producer call in `KafkaTelemetry.Source.StartActivity("kafka produce", …)`. The OTel TracerProvider in the Api project subscribes to `KafkaTelemetry.SourceName` so these spans flow to App Insights.
- Outbound HTTP retries / circuit breaker are configured at registration time in `ServiceConfigs.cs`. Don't add custom retry loops inside `ExternalApiClient` — let the resilience pipeline handle it.
- Implementations are auto-registered by Scrutor when their class name ends in `Publisher` or `Client`.

### Testing Requirements
- Unit tests should mock `IEventPublisher` / `IExternalApiClient` (the Domain ports) at the application boundary, not these concrete classes.
- Integration tests exercise the real producer against a Testcontainers Kafka broker.

### Common Patterns
- One adapter class per outbound concern; no god-objects.
- Logging uses `ILogger<T>` injected via constructor; structured logging only (use named placeholders, never string concatenation).

## Dependencies

### Internal
- `Hex.Scaffold.Domain` (port interfaces, telemetry source)

### External
- `Confluent.Kafka`
- `Microsoft.Extensions.Http.Resilience` (retry, circuit breaker, timeout)
- `OpenTelemetry.Api` (`ActivitySource`)

<!-- MANUAL: -->
