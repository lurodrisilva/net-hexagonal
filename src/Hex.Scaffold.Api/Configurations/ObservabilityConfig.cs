using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Hex.Scaffold.Api.Configurations;

public static class ObservabilityConfig
{
  public static IHostApplicationBuilder AddObservability(
    this IHostApplicationBuilder builder,
    string serviceName)
  {
    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4318";

    builder.Services.AddOpenTelemetry()
      .ConfigureResource(resource => resource
        .AddService(serviceName)
        .AddAttributes(new Dictionary<string, object>
        {
          { "deployment.environment", builder.Environment.EnvironmentName }
        }))
      .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(opts =>
        {
          opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/healthz")
                             && !ctx.Request.Path.StartsWithSegments("/ready");
        })
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts =>
        {
          opts.Endpoint = new Uri(otlpEndpoint);
          opts.Protocol = OtlpExportProtocol.HttpProtobuf;
        }))
      .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter((exporterOptions, readerOptions) =>
        {
          exporterOptions.Endpoint = new Uri(otlpEndpoint);
          exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
          readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10000;
        }));

    builder.Logging.AddOpenTelemetry(logging => logging
      .AddOtlpExporter(opts =>
      {
        opts.Endpoint = new Uri(otlpEndpoint);
        opts.Protocol = OtlpExportProtocol.HttpProtobuf;
      }));

    return builder;
  }
}
