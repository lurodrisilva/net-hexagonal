using Hex.Scaffold.Adapters.Inbound.Api.Samples;
using Hex.Scaffold.Adapters.Persistence.PostgreSql;
using Hex.Scaffold.Application.Behaviors;
using Hex.Scaffold.Application.Samples.Create;
using Hex.Scaffold.Domain.SampleAggregate;

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
        typeof(Sample).Assembly,               // Domain
        typeof(CreateSampleCommand).Assembly,  // Application
        typeof(AppDbContext).Assembly,         // Adapters.Persistence
        typeof(Create).Assembly,               // Adapters.Inbound
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
