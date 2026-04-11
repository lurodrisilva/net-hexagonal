namespace Hex.Scaffold.Domain.Ports.Outbound;

public interface ISampleReadModelRepository
{
  Task UpsertAsync(SampleReadModel document, CancellationToken cancellationToken = default);
  Task DeleteAsync(int sampleId, CancellationToken cancellationToken = default);
}
