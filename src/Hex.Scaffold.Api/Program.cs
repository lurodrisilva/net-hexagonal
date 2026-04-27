using Hex.Scaffold.Api.Configurations;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Observability (OpenTelemetry + Serilog)
builder.AddObservability("Hex.Scaffold");
builder.Host.UseSerilog((context, loggerConfig) =>
  loggerConfig.ReadFrom.Configuration(context.Configuration));

using var loggerFactory = LoggerFactory.Create(config => config.AddConsole());
var startupLogger = loggerFactory.CreateLogger<Program>();

// Core services
builder.Services.AddProblemDetails();
builder.Services.AddServiceConfigs(builder.Configuration, startupLogger);
builder.Services.AddHealthCheckServices(builder.Configuration);
builder.Services.AddRateLimitingServices(builder.Configuration);

// API
builder.Services.AddFastEndpoints()
  .SwaggerDocument(o =>
  {
    o.DocumentSettings = s =>
    {
      s.Title = "Hex.Scaffold API";
      s.Version = "v2";
      s.Description = "Stripe v2 Accounts API surface (Create / Retrieve / Update / List).";
    };
    o.ShortSchemaNames = true;
  });

var app = builder.Build();

await app.UseAppMiddlewareAsync();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
