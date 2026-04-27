using Azure.Monitor.OpenTelemetry.Exporter;
using Hex.Scaffold.Domain.Common;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Hex.Scaffold.Api.Configurations;

public static class ObservabilityConfig
{
  // Azure Monitor / Application Insights connection string. Env var takes
  // precedence over appsettings to match Microsoft's recommended production
  // pattern. Empty = feature disabled (Azure Monitor side silent; the OTel
  // pipeline still runs and feeds the classic AI SDK in-process channel
  // for Live Metrics).
  private const string AzureMonitorEnvVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
  private const string AzureMonitorConfigKey = "ApplicationInsights:ConnectionString";

  // Source name emitted by Npgsql 7+ for command/connection spans. Without
  // an explicit AddSource("Npgsql"), the TracerProvider drops every EF / Dapper
  // Postgres span on the floor — App Insights then shows the API node with
  // zero outbound dependency edges and the Application Map looks broken.
  // The classic AI SDK's DependencyTrackingTelemetryModule does NOT subscribe
  // to Npgsql ActivitySource events (it only knows DiagnosticListener-based
  // SqlClient), so this OTel subscription is the ONLY path Postgres edges
  // reach the App Map. Don't remove without replacing.
  private const string NpgsqlActivitySource = "Npgsql";

  public static IHostApplicationBuilder AddObservability(
    this IHostApplicationBuilder builder,
    string serviceName)
  {
    var appInsightsConnectionString =
      Environment.GetEnvironmentVariable(AzureMonitorEnvVar)
      ?? builder.Configuration[AzureMonitorConfigKey];
    var azureMonitorEnabled = !string.IsNullOrWhiteSpace(appInsightsConnectionString);

    // service.namespace + service.instance.id give the Application Map the per-pod
    // breakdown it needs. Both come from the chart (POD_NAME via downward API,
    // OTEL_SERVICE_NAMESPACE plain env). Falls back to HOSTNAME / "default" so
    // local runs still work without the env vars being set.
    var serviceNamespace = builder.Configuration["OpenTelemetry:ServiceNamespace"]
                           ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAMESPACE")
                           ?? "default";
    var serviceInstanceId = Environment.GetEnvironmentVariable("OTEL_SERVICE_INSTANCE_ID")
                            ?? Environment.GetEnvironmentVariable("HOSTNAME")
                            ?? Environment.MachineName;

    // OTel pipeline — wired ONLY to the Azure Monitor exporters. The OTLP
    // exporter pair was removed because every export attempt was POSTing to
    // a phantom collector (`http://otel-collector.observability.svc:4318`)
    // that this scaffold never deploys; classic AI's DependencyTrackingTele-
    // metryModule (re-enabled for Live Metrics — see below) was observing
    // those failed HTTP calls via its system-wide HttpClient DiagnosticSource
    // listener and surfacing them in Live Metrics as `Faulted` outbound
    // dependencies. Removing the OTLP exporters eliminates the faulted
    // dependency at the source.
    //
    // The TracerProvider / MeterProvider / LoggerProvider themselves remain
    // load-bearing: AddSource("Npgsql") and AddSource(KafkaTelemetry.SourceName)
    // are the ONLY mechanisms by which Postgres + Kafka spans reach App
    // Insights, since the classic AI SDK does not subscribe to arbitrary
    // ActivitySources. The AzureMonitor exporters serialize the OTel
    // pipeline output into App Insights ingest format and ship to
    // <region>.in.applicationinsights.azure.com directly, so the App Map
    // dependency edges and `dependencies` table rows for Postgres / Kafka
    // come from this path.
    builder.Services.AddOpenTelemetry()
      .ConfigureResource(resource => resource
        .AddService(serviceName, serviceNamespace, serviceVersion: null, autoGenerateServiceInstanceId: false, serviceInstanceId: serviceInstanceId)
        .AddAttributes(new Dictionary<string, object>
        {
          { "deployment.environment", builder.Environment.EnvironmentName }
        }))
      .WithTracing(tracing =>
      {
        tracing
          .AddAspNetCoreInstrumentation(opts =>
          {
            opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/healthz")
                               && !ctx.Request.Path.StartsWithSegments("/ready");
          })
          .AddHttpClientInstrumentation()
          // EF Core / Dapper -> Postgres dependency edges.
          .AddSource(NpgsqlActivitySource)
          // Kafka producer + consumer spans (Confluent.Kafka has no first-party
          // OTel package; KafkaEventPublisher and SampleEventConsumer emit
          // spans manually using this shared ActivitySource).
          .AddSource(KafkaTelemetry.SourceName);

        if (azureMonitorEnabled)
        {
          tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConnectionString);
        }
      })
      .WithMetrics(metrics =>
      {
        metrics
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation()
          .AddRuntimeInstrumentation();

        if (azureMonitorEnabled)
        {
          metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsConnectionString);
        }
      });

    builder.Logging.AddOpenTelemetry(logging =>
    {
      // Required for the Azure Monitor log exporter to populate `traces.message`
      // and `customDimensions` — without these two flags the exporter sees an
      // empty record and Application Insights ingest drops it silently.
      logging.IncludeFormattedMessage = true;
      logging.ParseStateValues = true;

      if (azureMonitorEnabled)
      {
        logging.AddAzureMonitorLogExporter(o => o.ConnectionString = appInsightsConnectionString);
      }
    });

    // Live Metrics (QuickPulse) — the Azure.Monitor.OpenTelemetry.Exporter
    // package does NOT support Live Metrics; that channel is Distro-only.
    // We register the classic Application Insights SDK to obtain
    // QuickPulseTelemetryModule (the 1-second control channel to
    // <region>.livediagnostics.monitor.azure.com derived from the
    // LiveEndpoint key in APPLICATIONINSIGHTS_CONNECTION_STRING).
    //
    // QuickPulse does NOT generate telemetry — it subscribes to whatever the
    // classic SDK's collectors emit into its in-process pipeline. The
    // collectors enabled below feed it.
    //
    // Trade-off: every HTTP request and every outbound dependency is ingested
    // twice (once via the AzureMonitor OTel exporter above, once via the
    // classic SDK). App Map deduplicates on cloud_RoleName so the topology
    // view stays clean; KQL counters need `summarize by sdkVersion` if you
    // want to disambiguate. Cleanest long-term fix is migrating to
    // Azure.Monitor.OpenTelemetry.AspNetCore (the Distro), which feeds Live
    // Metrics directly off the OTel pipeline with no double-ingestion.
    if (azureMonitorEnabled)
    {
      builder.Services.AddApplicationInsightsTelemetry(o =>
      {
        o.ConnectionString = appInsightsConnectionString;
        // Required for QuickPulse to surface the four golden signals.
        o.EnableRequestTrackingTelemetryModule    = true;
        o.EnableDependencyTrackingTelemetryModule = true;
        // EventCounter is the cross-platform .NET runtime counter source
        // (CPU, GC, exception count, working set). PerformanceCounter is
        // Windows-only and a no-op in our chiseled Linux runtime.
        o.EnableEventCounterCollectionModule       = true;
        o.EnablePerformanceCounterCollectionModule = false;
        // Off — these emit telemetry that does NOT feed Live Metrics and
        // only adds noise to the ingestion pipeline.
        o.EnableAppServicesHeartbeatTelemetryModule = false;
        o.EnableAzureInstanceMetadataTelemetryModule = false;
        o.EnableHeartbeat = false;
      });
    }

    return builder;
  }
}
