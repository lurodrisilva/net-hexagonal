namespace Hex.Scaffold.Domain.Ports.Outbound;

public interface IExternalApiClient
{
  Task<Result<TResponse>> SendAsync<TResponse>(
    string endpoint,
    HttpMethod method,
    object? body = null,
    CancellationToken cancellationToken = default);
}
