using Hex.Scaffold.Adapters.Persistence.Redis;
using Hex.Scaffold.Domain.Ports.Outbound;
using StackExchange.Redis;

namespace Hex.Scaffold.Adapters.Persistence.Extensions;

public static class RedisServiceExtensions
{
  public static IServiceCollection AddRedisServices(
    this IServiceCollection services,
    IConfiguration configuration,
    ILogger logger)
  {
    services.Configure<RedisOptions>(configuration.GetSection("Redis"));

    services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
      var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
      var configOptions = ConfigurationOptions.Parse(options.ConnectionString);
      configOptions.AbortOnConnectFail = false;
      configOptions.ConnectTimeout = 5000;
      configOptions.SyncTimeout = 5000;
      return ConnectionMultiplexer.Connect(configOptions);
    });

    services.AddScoped<ICacheService, RedisCacheService>();

    logger.LogInformation("Redis services registered.");
    return services;
  }
}
