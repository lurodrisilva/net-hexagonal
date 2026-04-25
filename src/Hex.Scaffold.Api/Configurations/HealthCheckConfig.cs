using Hex.Scaffold.Api.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Hex.Scaffold.Api.Configurations;

public static class HealthCheckConfig
{
  public static IServiceCollection AddHealthCheckServices(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Health checks are gated on the same FeaturesOptions selector that drives
    // DI registration in ServiceConfigs. Probing a backend the app never talks
    // to (e.g. MongoDB when persistence=postgres) would default to localhost,
    // hang for the driver's server-selection timeout (~30s for the Mongo
    // driver), and push the readiness probe past its deadline — making the pod
    // stay 0/Ready even though the app is fine.
    var features = configuration.GetSection(FeaturesOptions.SectionName).Get<FeaturesOptions>()
                   ?? new FeaturesOptions();

    var builder = services.AddHealthChecks()
      .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

    if (features.PostgresEnabled)
    {
      var pgConnection = configuration.GetConnectionString("PostgreSql") ?? "";
      builder.AddCheck("postgresql", () =>
      {
        try
        {
          using var connection = new Npgsql.NpgsqlConnection(pgConnection);
          connection.Open();
          return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
          return HealthCheckResult.Unhealthy("PostgreSQL connection failed.", ex);
        }
      }, tags: ["ready"]);
    }

    if (features.MongoEnabled)
    {
      var mongoConnection = configuration["MongoDB:ConnectionString"] ?? "";
      builder.AddCheck("mongodb", () =>
      {
        try
        {
          using var client = new MongoClient(mongoConnection);
          _ = client.ListDatabaseNames();
          return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
          return HealthCheckResult.Unhealthy("MongoDB ping failed.", ex);
        }
      }, tags: ["ready"]);
    }

    if (features.UseRedis)
    {
      var redisConnection = configuration["Redis:ConnectionString"] ?? "";
      builder.AddCheck("redis", () =>
      {
        try
        {
          var opts = ConfigurationOptions.Parse(redisConnection);
          opts.ConnectTimeout = 3000;
          using var connection = ConnectionMultiplexer.Connect(opts);
          connection.GetDatabase().Ping();
          return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
          return HealthCheckResult.Unhealthy("Redis ping failed.", ex);
        }
      }, tags: ["ready"]);
    }

    if (features.InboundKafkaEnabled || features.OutboundKafkaEnabled)
    {
      builder.AddCheck("kafka", () =>
      {
        try
        {
          var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
          var parts = bootstrapServers.Split(':');
          var host = parts[0];
          var port = parts.Length > 1 ? int.Parse(parts[1]) : 9092;
          using var client = new System.Net.Sockets.TcpClient();
          var connected = client.ConnectAsync(host, port).Wait(3000);
          return connected
            ? HealthCheckResult.Healthy("Kafka broker reachable")
            : HealthCheckResult.Degraded("Kafka broker unreachable (soft dependency)");
        }
        catch (Exception ex)
        {
          return HealthCheckResult.Degraded("Kafka unavailable (soft dependency).", ex);
        }
      }, tags: ["ready"]);
    }

    // Note: HTTP external API client health is not checked here.
    // The client itself is always available; resilience policies (retry/circuit breaker)
    // handle remote service unavailability at the call site.

    return services;
  }
}
