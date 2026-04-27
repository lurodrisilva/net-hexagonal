using Confluent.Kafka;
using Scrutor;
using Hex.Scaffold.Adapters.Inbound.Messaging;
using Hex.Scaffold.Adapters.Outbound.Http;
using Hex.Scaffold.Adapters.Outbound.Messaging;
using Hex.Scaffold.Adapters.Persistence.Common;
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
    // Feature selector: drives which adapters are wired. Read from the
    // "Features" configuration section (ConfigMap → environment variables
    // when deployed via the Helm chart).
    var features = configuration.GetSection(FeaturesOptions.SectionName).Get<FeaturesOptions>()
                   ?? new FeaturesOptions();
    features.Validate();
    services.AddSingleton(features);
    logger.LogInformation(
      "Features: Inbound={Inbound}, Outbound={Outbound}, Persistence={Persistence}, UseRedis={UseRedis}",
      features.InboundAdapter, features.OutboundAdapter, features.Persistence, features.UseRedis);

    // Persistence adapters — primary store is postgres OR mongo; redis is
    // optional and only valid alongside postgres (enforced in Validate()).
    if (features.PostgresEnabled)
    {
      services.AddPostgreSqlServices(configuration, logger);
    }
    if (features.MongoEnabled)
    {
      services.AddMongoDbServices(configuration, logger);
    }
    if (features.UseRedis)
    {
      services.AddRedisServices(configuration, logger);
    }
    else
    {
      // Domain/Application handlers inject ICacheService unconditionally.
      // Register a no-op fallback BEFORE the Scrutor scan below so its
      // RegistrationStrategy.Skip keeps RedisCacheService (matched by the
      // *Service suffix filter) from filling the port and then exploding
      // on its missing IConnectionMultiplexer dependency at request time.
      services.AddScoped<ICacheService, NullCacheService>();
      logger.LogInformation("Redis disabled — registered NullCacheService fallback.");
    }

    // Kafka — producer is registered when either the inbound consumer OR
    // the outbound publisher needs it. Consumer + BackgroundService register
    // only when inbound=kafka.
    var kafkaProducerNeeded = features.OutboundKafkaEnabled;
    var kafkaConsumerNeeded = features.InboundKafkaEnabled;
    if (kafkaProducerNeeded || kafkaConsumerNeeded)
    {
      services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
    }
    if (kafkaProducerNeeded)
    {
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
      services.AddScoped<IEventPublisher, KafkaEventPublisher>();
    }
    else
    {
      // SampleEventPublishHandler injects IEventPublisher unconditionally.
      // Register a no-op fallback BEFORE the Scrutor scan below so its
      // RegistrationStrategy.Skip keeps KafkaEventPublisher (matched by
      // the *Publisher suffix filter) from filling the port and then
      // exploding on its missing IProducer<string,string> dependency the
      // moment any command triggers a domain notification.
      services.AddScoped<IEventPublisher, NoOpEventPublisher>();
      logger.LogInformation("Outbound Kafka disabled — registered NoOpEventPublisher fallback.");
    }
    if (kafkaConsumerNeeded)
    {
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
      services.AddHostedService<SampleEventConsumer>();
    }

    // HTTP Resilient Client — always available because outbound REST maps to
    // this adapter, and inbound REST endpoints may call out to it as well.
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

    // Scrutor: scan adapter assemblies for any remaining port implementations.
    // Explicit registrations above take precedence (RegistrationStrategy.Skip).
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
