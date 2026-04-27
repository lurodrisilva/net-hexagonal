using Azure.Monitor.OpenTelemetry.Exporter;
using Hex.Scaffold.Domain.Common;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Hex.Scaffold.Api.Configurations;

public static class ObservabilityConfig
{
  // Azure Monitor / Application Insights connection string. Env var takes
  // precedence over appsettings to match Microsoft's recommended production
  // pattern. Empty = feature disabled (OTLP pipeline still runs).
  private const string AzureMonitorEnvVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
  private const string AzureMonitorConfigKey = "ApplicationInsights:ConnectionString";

  // Source name emitted by Npgsql 7+ for command/connection spans. Without
  // an explicit AddSource("Npgsql"), the TracerProvider drops every EF / Dapper
  // Postgres span on the floor — App Insights then shows the API node with
  // zero outbound dependency edges and the Application Map looks broken.
  private const string NpgsqlActivitySource = "Npgsql";

  // OTel resource attribute names per the semantic conventions, used for
  // App Insights Cloud role name / Cloud role instance derivation. See
  // https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-configuration
  private const string ServiceNamespaceAttr = "service.namespace";
  private const string ServiceInstanceAttr = "service.instance.id";

  public static IHostApplicationBuilder AddObservability(
    this IHostApplicationBuilder builder,
    string serviceName)
  {
    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4318";
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
          .AddSource(KafkaTelemetry.SourceName)
          .AddOtlpExporter(opts =>
          {
            opts.Endpoint = new Uri(otlpEndpoint);
            opts.Protocol = OtlpExportProtocol.HttpProtobuf;
          });

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
          .AddRuntimeInstrumentation()
          .AddOtlpExporter((exporterOptions, readerOptions) =>
          {
            exporterOptions.Endpoint = new Uri(otlpEndpoint);
            exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
            readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10000;
          });

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

      logging.AddOtlpExporter(opts =>
      {
        opts.Endpoint = new Uri(otlpEndpoint);
        opts.Protocol = OtlpExportProtocol.HttpProtobuf;
      });

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
    // classic SDK's collectors emit into its pipeline. A previous revision
    // disabled every collector to "keep the pipeline clean" and ended up
    // streaming zeros to Live Metrics: request rate, request duration,
    // dependency rate / duration, and CPU / memory all rendered empty in the
    // portal even though the OTel side was ingesting fine.
    //
    // Re-enable the collectors QuickPulse needs. Trade-off: every request
    // and every outbound dependency is now ingested twice (once via the
    // OTel exporter above, once via the classic SDK), roughly doubling
    // App Insights ingest cost for those signal types and producing two
    // entries per call in the `requests` / `dependencies` tables. The App
    // Map deduplicates on cloud_RoleName so the topology view stays clean,
    // but KQL counters (e.g. `requests | count`) need a `summarize by
    // sdkVersion` if you want to disambiguate. Accepted as the cost of
    // working Live Metrics until we migrate to the AzureMonitor Distro
    // (Azure.Monitor.OpenTelemetry.AspNetCore), which feeds Live Metrics
    // directly off the OTel pipeline with no double-ingestion.
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
