using Hex.Scaffold.Application.Samples.Get;

namespace Hex.Scaffold.Adapters.Inbound.Api.Samples;

public class GetExternalInfo(IMediator mediator)
  : Endpoint<GetExternalInfoRequest, Ok<string>>
{
  public override void Configure()
  {
    Get("/samples/external-info");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Get external info (demonstrates outbound HTTP adapter)";
      s.Description = "Calls an external API via the resilient HTTP client adapter.";
      s.Responses[200] = "External info retrieved";
    });
    Tags("Samples");
  }

  public override async Task<Ok<string>> ExecuteAsync(
    GetExternalInfoRequest request,
    CancellationToken ct)
  {
    var result = await mediator.Send(
      new GetExternalSampleInfoQuery(request.Endpoint ?? "/get"), ct);

    return TypedResults.Ok(result.IsSuccess ? result.Value : result.Errors.FirstOrDefault() ?? "error");
  }
}

public class GetExternalInfoRequest
{
  public string? Endpoint { get; set; } = "/get";
}
