using Hex.Scaffold.Domain.SampleAggregate;
using Hex.Scaffold.Domain.SampleAggregate.Events;

namespace Hex.Scaffold.Application.Samples.Create;

public sealed class CreateSampleHandler(
  IRepository<Sample> _repository,
  IMediator _mediator,
  ILogger<CreateSampleHandler> _logger)
  : ICommandHandler<CreateSampleCommand, Result<SampleId>>
{
  public async ValueTask<Result<SampleId>> Handle(
    CreateSampleCommand command,
    CancellationToken cancellationToken)
  {
    _logger.LogInformation("Creating Sample with name {SampleName}", command.Name);

    var sample = new Sample(command.Name);
    if (command.Description is not null)
      sample.UpdateDescription(command.Description);

    var created = await _repository.AddAsync(sample, cancellationToken);

    await _mediator.Publish(new SampleCreatedEvent(created), cancellationToken);

    return created.Id;
  }
}
