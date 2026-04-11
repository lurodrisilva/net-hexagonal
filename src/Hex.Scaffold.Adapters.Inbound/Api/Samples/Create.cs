using Hex.Scaffold.Adapters.Inbound.Api.Extensions;
using Hex.Scaffold.Application.Samples.Create;
using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Adapters.Inbound.Api.Samples;

public class Create(IMediator mediator)
  : Endpoint<CreateSampleRequest,
      Results<Created<CreateSampleResponse>, ValidationProblem, ProblemHttpResult>>
{
  public override void Configure()
  {
    Post("/samples");
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Create a new sample";
      s.Description = "Creates a new Sample resource.";
      s.Responses[201] = "Sample created successfully";
      s.Responses[400] = "Invalid input";
    });
    Tags("Samples");
  }

  public override async Task<Results<Created<CreateSampleResponse>, ValidationProblem, ProblemHttpResult>>
    ExecuteAsync(CreateSampleRequest request, CancellationToken ct)
  {
    var command = new CreateSampleCommand(
      SampleName.From(request.Name!),
      request.Description);

    var result = await mediator.Send(command, ct);

    return result.ToCreatedResult(
      id => $"/samples/{id.Value}",
      id => new CreateSampleResponse(id.Value, request.Name!));
  }
}

public class CreateSampleRequest
{
  public const string Route = "/samples";
  public string? Name { get; set; }
  public string? Description { get; set; }
}

public class CreateSampleValidator : Validator<CreateSampleRequest>
{
  public CreateSampleValidator()
  {
    RuleFor(x => x.Name)
      .NotEmpty().WithMessage("Name is required.")
      .MaximumLength(SampleName.MaxLength);
  }
}

public class CreateSampleResponse(int id, string name)
{
  public int Id { get; set; } = id;
  public string Name { get; set; } = name;
}
