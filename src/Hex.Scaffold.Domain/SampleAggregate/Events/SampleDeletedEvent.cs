namespace Hex.Scaffold.Domain.SampleAggregate.Events;

public sealed class SampleDeletedEvent(SampleId sampleId) : DomainEventBase
{
  public SampleId SampleId { get; init; } = sampleId;
}
