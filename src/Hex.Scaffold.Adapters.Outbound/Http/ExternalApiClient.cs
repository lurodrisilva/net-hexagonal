using System.Text;
using System.Text.Json;
using Hex.Scaffold.Domain.Common;
using Hex.Scaffold.Domain.Ports.Outbound;

namespace Hex.Scaffold.Adapters.Outbound.Http;

public sealed class ExternalApiClient(
  IHttpClientFactory _httpClientFactory,
  ILogger<ExternalApiClient> _logger) : IExternalApiClient
{
  private const string ClientName = "ExternalApi";

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public async Task<Result<TResponse>> SendAsync<TResponse>(
    string endpoint,
    HttpMethod method,
    object? body = null,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var client = _httpClientFactory.CreateClient(ClientName);
      var request = new HttpRequestMessage(method, endpoint);

      if (body is not null)
      {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
      }

      var response = await client.SendAsync(request, cancellationToken);

      if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        return Result<TResponse>.NotFound();

      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning(
          "External API returned {StatusCode} for {Endpoint}",
          (int)response.StatusCode, endpoint);
        return Result<TResponse>.Error($"External API error: {(int)response.StatusCode}");
      }

      var content = await response.Content.ReadAsStringAsync(cancellationToken);
      var result = JsonSerializer.Deserialize<TResponse>(content, JsonOptions);

      if (result is null)
        return Result<TResponse>.Error("Failed to deserialize response.");

      return Result<TResponse>.Success(result);
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "HTTP request failed for {Endpoint}", endpoint);
      return Result<TResponse>.Error($"Request failed: {ex.Message}");
    }
  }
}
