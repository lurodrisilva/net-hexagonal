using Hex.Scaffold.Adapters.Persistence.Common;
using Hex.Scaffold.Adapters.Persistence.PostgreSql;
using Hex.Scaffold.Adapters.Persistence.PostgreSql.Queries;
using Hex.Scaffold.Application.Samples.List;
using Hex.Scaffold.Domain.Common;
using Hex.Scaffold.Domain.Interfaces;
using Hex.Scaffold.Domain.Services;

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

    services.AddDbContext<AppDbContext>((provider, options) =>
    {
      var interceptor = provider.GetRequiredService<EventDispatcherInterceptor>();
      options.UseNpgsql(connectionString, npgsqlOptions =>
      {
        npgsqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
        npgsqlOptions.CommandTimeout(60);
      });
      options.AddInterceptors(interceptor);
    });

    services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
    services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
    services.AddScoped<IListSamplesQueryService, ListSamplesQueryService>();
    services.AddScoped<IDeleteSampleService, DeleteSampleService>();

    logger.LogInformation("PostgreSQL services registered.");
    return services;
  }
}
