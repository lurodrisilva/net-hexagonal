using Hex.Scaffold.Domain.Ports.Outbound;

namespace Hex.Scaffold.Application.Samples.Get;

public sealed class GetExternalSampleInfoHandler(
  IExternalApiClient _externalApiClient,
  ILogger<GetExternalSampleInfoHandler> _logger)
  : IQueryHandler<GetExternalSampleInfoQuery, Result<string>>
{
  public async ValueTask<Result<string>> Handle(
    GetExternalSampleInfoQuery request,
    CancellationToken cancellationToken)
  {
    _logger.LogInformation("Fetching external info from {Endpoint}", request.Endpoint);

    var result = await _externalApiClient.SendAsync<string>(
      request.Endpoint,
      HttpMethod.Get,
      null,
      cancellationToken);

    return result;
  }
}
