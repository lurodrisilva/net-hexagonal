using Hex.Scaffold.Adapters.Inbound.Api.Extensions;
using Hex.Scaffold.Application.Samples;
using Hex.Scaffold.Application.Samples.Update;
using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Samples;

public class Update(IMediator mediator)
  : Endpoint<UpdateSampleRequest,
      Results<Ok<SampleRecord>, NotFound, ProblemHttpResult>,
      UpdateSampleMapper>
{
  public override void Configure()
  {
    Put("/samples/{sampleId:int}");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Update a sample";
      s.Responses[200] = "Sample updated";
      s.Responses[404] = "Sample not found";
    });
    Tags("Samples");
  }

  public override async Task<Results<Ok<SampleRecord>, NotFound, ProblemHttpResult>>
    ExecuteAsync(UpdateSampleRequest request, CancellationToken ct)
  {
    var command = new UpdateSampleCommand(
      SampleId.From(request.SampleId),
      SampleName.From(request.Name!),
      request.Description);

    var result = await mediator.Send(command, ct);
    return result.ToUpdateResult(Map.FromEntity);
  }
}

public class UpdateSampleRequest
{
  public int SampleId { get; set; }
  public string? Name { get; set; }
  public string? Description { get; set; }
}

public class UpdateSampleValidator : Validator<UpdateSampleRequest>
{
  public UpdateSampleValidator()
  {
    RuleFor(x => x.Name)
      .NotEmpty().WithMessage("Name is required.")
      .MaximumLength(SampleName.MaxLength);
  }
}

public sealed class UpdateSampleMapper : Mapper<UpdateSampleRequest, SampleRecord, SampleDto>
{
  public override SampleRecord FromEntity(SampleDto e)
    => new(e.Id.Value, e.Name.Value, e.Status.Name, e.Description);
}
