using Hex.Scaffold.Domain.Interfaces;

namespace Hex.Scaffold.Application.Samples.Delete;

public sealed class DeleteSampleHandler(IDeleteSampleService _service)
  : ICommandHandler<DeleteSampleCommand, Result>
{
  public ValueTask<Result> Handle(
    DeleteSampleCommand request,
    CancellationToken cancellationToken) =>
    _service.DeleteSampleAsync(request.SampleId, cancellationToken);
}
