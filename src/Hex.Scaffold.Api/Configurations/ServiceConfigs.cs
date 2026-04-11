using Confluent.Kafka;
using Scrutor;
using Hex.Scaffold.Adapters.Inbound.Messaging;
using Hex.Scaffold.Adapters.Outbound.Http;
using Hex.Scaffold.Adapters.Outbound.Messaging;
using Hex.Scaffold.Adapters.Persistence.Extensions;
using Hex.Scaffold.Api.Options;
using Hex.Scaffold.Domain.Ports.Outbound;
using Microsoft.Extensions.Http.Resilience;

namespace Hex.Scaffold.Api.Configurations;

public static class ServiceConfigs
{
  public static IServiceCollection AddServiceConfigs(
    this IServiceCollection services,
    IConfiguration configuration,
    Microsoft.Extensions.Logging.ILogger logger)
  {
    // Persistence adapters
    services.AddPostgreSqlServices(configuration, logger);
    services.AddMongoDbServices(configuration, logger);
    services.AddRedisServices(configuration, logger);

    // Kafka
    services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
    services.AddSingleton<IProducer<string, string>>(sp =>
    {
      var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
      var config = new ProducerConfig
      {
        BootstrapServers = options.BootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true
      };
      return new ProducerBuilder<string, string>(config).Build();
    });
    services.AddSingleton<IConsumer<string, string>>(sp =>
    {
      var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
      var config = new ConsumerConfig
      {
        BootstrapServers = options.BootstrapServers,
        GroupId = options.ConsumerGroupId,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false
      };
      return new ConsumerBuilder<string, string>(config).Build();
    });
    services.AddScoped<IEventPublisher, KafkaEventPublisher>();
    services.AddHostedService<SampleEventConsumer>();

    // HTTP Resilient Client — uses existing ExternalApiOptions from outbound adapter
    services.Configure<ExternalApiOptions>(configuration.GetSection("ExternalApi"));
    services.AddHttpClient("ExternalApi", (sp, client) =>
    {
      var options = sp.GetRequiredService<IOptions<ExternalApiOptions>>().Value;
      client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler();
    services.AddScoped<IExternalApiClient, ExternalApiClient>();

    // Mediator
    services.AddMediatorServices(logger);

    // Scrutor: scan adapter assemblies for any remaining port implementations
    // (explicit registrations above take precedence — RegistrationStrategy.Skip skips already-registered services)
    services.Scan(scan => scan
      .FromAssembliesOf(
        typeof(Hex.Scaffold.Adapters.Persistence.PostgreSql.AppDbContext),
        typeof(Hex.Scaffold.Adapters.Outbound.Messaging.KafkaEventPublisher))
      .AddClasses(classes => classes
        .InNamespaces(
          "Hex.Scaffold.Adapters.Persistence",
          "Hex.Scaffold.Adapters.Outbound")
        .Where(t =>
          t.Name.EndsWith("Service") ||
          t.Name.EndsWith("Repository") ||
          t.Name.EndsWith("Publisher") ||
          t.Name.EndsWith("Client")))
      .UsingRegistrationStrategy(RegistrationStrategy.Skip)
      .AsImplementedInterfaces()
      .WithScopedLifetime());

    logger.LogInformation("All services registered.");
    return services;
  }
}
