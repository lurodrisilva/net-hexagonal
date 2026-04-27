using Hex.Scaffold.Adapters.Inbound.Api.Accounts;
using Hex.Scaffold.Adapters.Persistence.PostgreSql;
using Hex.Scaffold.Application.Accounts.Create;
using Hex.Scaffold.Application.Behaviors;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Api.Configurations;

public static class MediatorConfig
{
  public static IServiceCollection AddMediatorServices(
    this IServiceCollection services,
    Microsoft.Extensions.Logging.ILogger logger)
  {
    services.AddMediator(options =>
    {
      options.ServiceLifetime = ServiceLifetime.Scoped;
      options.Assemblies =
      [
        typeof(Account).Assembly,                // Domain
        typeof(CreateAccountCommand).Assembly,   // Application
        typeof(AppDbContext).Assembly,           // Adapters.Persistence
        typeof(CreateAccount).Assembly,          // Adapters.Inbound
      ];
      options.PipelineBehaviors =
      [
        typeof(LoggingBehavior<,>)
      ];
    });

    logger.LogInformation("Mediator services registered.");
    return services;
  }
}
