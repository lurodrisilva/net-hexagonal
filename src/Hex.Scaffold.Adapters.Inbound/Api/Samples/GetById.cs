using Hex.Scaffold.Adapters.Inbound.Api.Extensions;
using Hex.Scaffold.Application.Samples;
using Hex.Scaffold.Application.Samples.Get;
using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Samples;

public class GetById(IMediator mediator)
  : Endpoint<GetSampleByIdRequest,
      Results<Ok<SampleRecord>, NotFound, ProblemHttpResult>,
      GetSampleByIdMapper>
{
  public override void Configure()
  {
    Get("/samples/{sampleId:int}");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Get a sample by ID";
      s.Responses[200] = "Sample found";
      s.Responses[404] = "Sample not found";
    });
    Tags("Samples");
  }

  public override async Task<Results<Ok<SampleRecord>, NotFound, ProblemHttpResult>>
    ExecuteAsync(GetSampleByIdRequest request, CancellationToken ct)
  {
    var result = await mediator.Send(
      new GetSampleQuery(SampleId.From(request.SampleId)), ct);

    return result.ToGetByIdResult(Map.FromEntity);
  }
}

public class GetSampleByIdRequest
{
  public const string Route = "/samples/{sampleId:int}";
  public int SampleId { get; set; }
}

public sealed class GetSampleByIdMapper
  : Mapper<GetSampleByIdRequest, SampleRecord, SampleDto>
{
  public override SampleRecord FromEntity(SampleDto e)
    => new(e.Id.Value, e.Name.Value, e.Status.Name, e.Description);
}
