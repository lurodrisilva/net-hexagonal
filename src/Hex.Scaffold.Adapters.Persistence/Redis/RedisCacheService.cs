using System.Text.Json;
using Hex.Scaffold.Domain.Ports.Outbound;
using StackExchange.Redis;

namespace Hex.Scaffold.Adapters.Persistence.Redis;

public sealed class RedisCacheService(
  IConnectionMultiplexer _redis,
  ILogger<RedisCacheService> _logger) : ICacheService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
  {
    try
    {
      var db = _redis.GetDatabase();
      var value = await db.StringGetAsync(key);

      if (!value.HasValue)
        return default;

      return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
    }
    catch (RedisConnectionException ex)
    {
      _logger.LogWarning(ex, "Redis unavailable on GetAsync for key {Key}. Returning cache miss.", key);
      return default;
    }
  }

  public async Task SetAsync<T>(
    string key,
    T value,
    TimeSpan? expiration = null,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var db = _redis.GetDatabase();
      var serialized = JsonSerializer.Serialize(value, JsonOptions);
      await db.StringSetAsync(key, serialized, expiration);
    }
    catch (RedisConnectionException ex)
    {
      _logger.LogWarning(ex, "Redis unavailable on SetAsync for key {Key}. Skipping cache write.", key);
    }
  }

  public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
  {
    try
    {
      var db = _redis.GetDatabase();
      await db.KeyDeleteAsync(key);
    }
    catch (RedisConnectionException ex)
    {
      _logger.LogWarning(ex, "Redis unavailable on RemoveAsync for key {Key}. Skipping.", key);
    }
  }
}
