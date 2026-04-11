using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Hex.Scaffold.Api.Configurations;

public static class RateLimitingConfig
{
  public static IServiceCollection AddRateLimitingServices(this IServiceCollection services)
  {
    services.AddRateLimiter(options =>
    {
      options.RejectionStatusCode = 429;

      options.AddFixedWindowLimiter("default", limiterOptions =>
      {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
      });

      options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
          partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
          factory: partition => new FixedWindowRateLimiterOptions
          {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
          }));
    });

    return services;
  }
}
