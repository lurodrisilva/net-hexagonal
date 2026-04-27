using Hex.Scaffold.Adapters.Persistence.Common;
using Hex.Scaffold.Adapters.Persistence.PostgreSql;
using Hex.Scaffold.Adapters.Persistence.PostgreSql.Queries;
using Hex.Scaffold.Application.Accounts.List;
using Hex.Scaffold.Domain.Common;
using Npgsql;

namespace Hex.Scaffold.Adapters.Persistence.Extensions;

public static class PostgreSqlServiceExtensions
{
  public static IServiceCollection AddPostgreSqlServices(
    this IServiceCollection services,
    IConfiguration configuration,
    ILogger logger)
  {
    var connectionString = configuration.GetConnectionString("PostgreSql")
      ?? throw new InvalidOperationException("PostgreSql connection string is required.");

    services.AddScoped<EventDispatcherInterceptor>();
    services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();

    // Building the NpgsqlDataSource with EnableDynamicJson() is the only
    // path that lets EF map a `string` property onto a `jsonb` column
    // without a custom value converter — the Account aggregate stores
    // Stripe's nested objects (configuration, identity, defaults,
    // requirements, future_requirements, metadata) as raw JSON strings,
    // and the parameter binding has to know to send them as `jsonb`. Without
    // the flag Npgsql rejects the parameter at write time with
    // "json text 'null' is not jsonb-compatible" or similar.
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.EnableDynamicJson();
    var dataSource = dataSourceBuilder.Build();
    services.AddSingleton(dataSource);

    services.AddDbContext<AppDbContext>((provider, options) =>
    {
      var interceptor = provider.GetRequiredService<EventDispatcherInterceptor>();
      var ds = provider.GetRequiredService<NpgsqlDataSource>();
      options.UseNpgsql(ds, npgsqlOptions =>
      {
        npgsqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
        npgsqlOptions.CommandTimeout(60);
      });
      options.AddInterceptors(interceptor);
    });

    services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
    services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
    services.AddScoped<IListAccountsQueryService, ListAccountsQueryService>();

    logger.LogInformation("PostgreSQL services registered.");
    return services;
  }
}
