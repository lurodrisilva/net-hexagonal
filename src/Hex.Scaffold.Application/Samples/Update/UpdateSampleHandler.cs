using Hex.Scaffold.Domain.SampleAggregate;
using Hex.Scaffold.Domain.SampleAggregate.Specifications;

namespace Hex.Scaffold.Application.Samples.Update;

public sealed class UpdateSampleHandler(
  IRepository<Sample> _repository,
  ILogger<UpdateSampleHandler> _logger)
  : ICommandHandler<UpdateSampleCommand, Result<SampleDto>>
{
  public async ValueTask<Result<SampleDto>> Handle(
    UpdateSampleCommand command,
    CancellationToken cancellationToken)
  {
    _logger.LogInformation("Updating Sample {SampleId}", command.Id);

    var spec = new SampleByIdSpec(command.Id);
    var sample = await _repository.FirstOrDefaultAsync(spec, cancellationToken);
    if (sample is null) return Result.NotFound();

    sample.UpdateName(command.Name)
          .UpdateDescription(command.Description);

    await _repository.UpdateAsync(sample, cancellationToken);

    return new SampleDto(sample.Id, sample.Name, sample.Status, sample.Description);
  }
}
