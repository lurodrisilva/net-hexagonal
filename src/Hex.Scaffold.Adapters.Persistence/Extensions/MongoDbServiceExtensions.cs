using Hex.Scaffold.Adapters.Persistence.MongoDb;
using MongoDB.Driver;

namespace Hex.Scaffold.Adapters.Persistence.Extensions;

// Mongo registration retained as the no-op skeleton for the
// `features.persistence=mongo` selector path so the Helm validate-at-render
// invariant still holds. The Sample read-model + repository this used to
// register was removed when the Sample aggregate was replaced by Account
// (which lives in Postgres only); a future PR can wire an
// IAccountReadModelRepository here without touching the registration shape.
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

    logger.LogInformation("MongoDB client registered (no read-model repository wired yet).");
    return services;
  }
}
