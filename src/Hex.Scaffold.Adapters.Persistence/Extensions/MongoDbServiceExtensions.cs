using Hex.Scaffold.Adapters.Persistence.MongoDb;
using Hex.Scaffold.Domain.Ports.Outbound;
using MongoDB.Driver;

namespace Hex.Scaffold.Adapters.Persistence.Extensions;

public static class MongoDbServiceExtensions
{
  public static IServiceCollection AddMongoDbServices(
    this IServiceCollection services,
    IConfiguration configuration,
    ILogger logger)
  {
    services.Configure<MongoDbOptions>(configuration.GetSection("MongoDB"));

    services.AddSingleton<IMongoClient>(sp =>
    {
      var options = sp.GetRequiredService<IOptions<MongoDbOptions>>().Value;
      var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
      settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
      settings.ConnectTimeout = TimeSpan.FromSeconds(10);
      return new MongoClient(settings);
    });

    services.AddScoped<ISampleReadModelRepository, SampleReadModelRepository>();

    logger.LogInformation("MongoDB services registered.");
    return services;
  }
}
