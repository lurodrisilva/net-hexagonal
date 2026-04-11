namespace Hex.Scaffold.Domain.SampleAggregate.Events;

public sealed class SampleUpdatedEvent(Sample sample) : DomainEventBase
{
  public Sample Sample { get; init; } = sample;
}
