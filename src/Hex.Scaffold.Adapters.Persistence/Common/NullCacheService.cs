using Hex.Scaffold.Domain.Ports.Outbound;

namespace Hex.Scaffold.Adapters.Persistence.Common;

// Fallback ICacheService used when features.UseRedis = false so callers
// (GetSampleHandler, SampleEventPublishHandler) can keep injecting
// ICacheService unconditionally. Without this, the Scrutor scan in
// ServiceConfigs picks up RedisCacheService (it ends in *Service), DI
// fails to resolve its IConnectionMultiplexer, and every read/cache-
// invalidation path explodes.
public sealed class NullCacheService : ICacheService
{
  public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    => Task.FromResult<T?>(default);

  public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    => Task.CompletedTask;

  public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    => Task.CompletedTask;
}
