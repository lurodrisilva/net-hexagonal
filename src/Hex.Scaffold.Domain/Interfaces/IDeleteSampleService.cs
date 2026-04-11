using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Domain.Interfaces;

public interface IDeleteSampleService
{
  ValueTask<Result> DeleteSampleAsync(SampleId id, CancellationToken cancellationToken = default);
}
