using Hex.Scaffold.Adapters.Inbound.Api.Extensions;
using Hex.Scaffold.Application.Samples.Delete;
using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Samples;

public class Delete(IMediator mediator)
  : Endpoint<DeleteSampleRequest,
      Results<NoContent, NotFound, ProblemHttpResult>>
{
  public override void Configure()
  {
    Delete("/samples/{sampleId:int}");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Delete a sample";
      s.Responses[204] = "Sample deleted";
      s.Responses[404] = "Sample not found";
    });
    Tags("Samples");
  }

  public override async Task<Results<NoContent, NotFound, ProblemHttpResult>>
    ExecuteAsync(DeleteSampleRequest request, CancellationToken ct)
  {
    var command = new DeleteSampleCommand(SampleId.From(request.SampleId));
    var result = await mediator.Send(command, ct);
    return result.ToDeleteResult();
  }
}

public class DeleteSampleRequest
{
  public int SampleId { get; set; }
}
