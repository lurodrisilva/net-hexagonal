using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Domain.Ports.Outbound;

public interface ISampleIdGenerator
{
  Task<SampleId> NextAsync(CancellationToken cancellationToken = default);
}
