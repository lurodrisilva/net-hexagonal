namespace Hex.Scaffold.Tests.Integration.Fixtures;

[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture> { }

public sealed class IntegrationTestFixture : IAsyncLifetime
{
  private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
    .WithDatabase("hex-scaffold-test")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

  private readonly RedisContainer _redis = new RedisBuilder().Build();

  public WebApplicationFactory<Program>? Factory { get; private set; }
  public string? PostgreSqlConnectionString { get; private set; }
  public string? RedisConnectionString { get; private set; }

  public async Task InitializeAsync()
  {
    await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

    PostgreSqlConnectionString = _postgres.GetConnectionString();
    RedisConnectionString = _redis.GetConnectionString();

    Factory = new WebApplicationFactory<Program>()
      .WithWebHostBuilder(host =>
      {
        host.UseEnvironment("Testing");
        host.ConfigureAppConfiguration((ctx, config) =>
        {
          config.AddInMemoryCollection(new Dictionary<string, string?>
          {
            ["ConnectionStrings:PostgreSql"] = PostgreSqlConnectionString,
            ["Redis:ConnectionString"] = RedisConnectionString,
            ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
            ["Kafka:BootstrapServers"] = "localhost:9092",
            ["Database:ApplyMigrationsOnStartup"] = "false"
          });
        });
      });

    // Warm up the factory — triggers DI registration and app startup
    _ = Factory.CreateClient();
  }

  public async Task DisposeAsync()
  {
    if (Factory is not null)
      await Factory.DisposeAsync();

    await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
  }
}
