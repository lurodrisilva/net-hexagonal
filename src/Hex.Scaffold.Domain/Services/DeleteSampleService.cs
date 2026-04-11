using Hex.Scaffold.Domain.Interfaces;
using Hex.Scaffold.Domain.SampleAggregate;
using Hex.Scaffold.Domain.SampleAggregate.Events;

namespace Hex.Scaffold.Domain.Services;

public sealed class DeleteSampleService(
  IRepository<Sample> _repository,
  IMediator _mediator,
  ILogger<DeleteSampleService> _logger) : IDeleteSampleService
{
  public async ValueTask<Result> DeleteSampleAsync(SampleId id, CancellationToken cancellationToken = default)
  {
    _logger.LogInformation("Deleting Sample {SampleId}", id);

    var sample = await _repository.GetByIdAsync(id, cancellationToken);
    if (sample is null) return Result.NotFound();

    await _repository.DeleteAsync(sample, cancellationToken);
    await _mediator.Publish(new SampleDeletedEvent(id), cancellationToken);

    return Result.Success();
  }
}
