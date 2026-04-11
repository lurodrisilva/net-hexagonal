namespace Hex.Scaffold.Domain.SampleAggregate.Events;

public sealed class SampleCreatedEvent(Sample sample) : DomainEventBase
{
  public Sample Sample { get; init; } = sample;
}
