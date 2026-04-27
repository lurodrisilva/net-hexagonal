using Hex.Scaffold.Domain.Ports.Outbound;

namespace Hex.Scaffold.Adapters.Outbound.Messaging;

// Fallback IEventPublisher used when features.outbound != "kafka" so the
// composition root never has to leave the port unbound. Domain handlers
// (SampleEventPublishHandler) inject IEventPublisher unconditionally —
// without this fallback the Scrutor scan in ServiceConfigs picks up
// KafkaEventPublisher (it ends in *Publisher), DI fails to resolve its
// IProducer<string,string> dependency, and every command that triggers a
// notification crashes. Drop the event with a debug log; the inbound
// consumer is also off in this configuration so there is no peer to
// receive it.
public sealed class NoOpEventPublisher(ILogger<NoOpEventPublisher> _logger) : IEventPublisher
{
  public ValueTask PublishAsync<TEvent>(string topic, TEvent @event, CancellationToken cancellationToken = default)
    where TEvent : class
  {
    _logger.LogDebug(
      "Outbound messaging disabled — dropping {EventType} for topic {Topic}",
      typeof(TEvent).Name, topic);
    return ValueTask.CompletedTask;
  }
}
