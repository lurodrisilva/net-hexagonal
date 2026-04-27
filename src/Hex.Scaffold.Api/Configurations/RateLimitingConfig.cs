using System.Threading.RateLimiting;
using Hex.Scaffold.Api.Options;
using Microsoft.AspNetCore.RateLimiting;

namespace Hex.Scaffold.Api.Configurations;

public static class RateLimitingConfig
{
  public static IServiceCollection AddRateLimitingServices(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    var rl = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
             ?? new RateLimitOptions();
    services.AddSingleton(rl);

    var window = TimeSpan.FromSeconds(Math.Max(1, rl.WindowSeconds));

    services.AddRateLimiter(options =>
    {
      options.RejectionStatusCode = 429;

      options.AddFixedWindowLimiter("default", limiterOptions =>
      {
        limiterOptions.PermitLimit = rl.PermitLimit;
        limiterOptions.Window = window;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = rl.QueueLimit;
      });

      options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
          partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
          factory: _ => new FixedWindowRateLimiterOptions
          {
            PermitLimit = rl.PermitLimit,
            Window = window,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = rl.QueueLimit
          }));
    });

    return services;
  }
}
