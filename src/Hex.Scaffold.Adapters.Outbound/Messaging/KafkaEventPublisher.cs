using System.Text.Json;
using Confluent.Kafka;
using Hex.Scaffold.Domain.Ports.Outbound;

namespace Hex.Scaffold.Adapters.Outbound.Messaging;

public sealed class KafkaEventPublisher(
  IProducer<string, string> _producer,
  ILogger<KafkaEventPublisher> _logger) : IEventPublisher
{
  // TODO: Implement Transactional Outbox pattern for production use.
  // Currently uses fire-and-forget with eventual consistency.
  public async ValueTask PublishAsync<TEvent>(
    string topic,
    TEvent @event,
    CancellationToken cancellationToken = default)
    where TEvent : class
  {
    try
    {
      var serialized = JsonSerializer.Serialize(@event);
      var message = new Message<string, string>
      {
        Key = typeof(TEvent).Name,
        Value = serialized
      };

      var result = await _producer.ProduceAsync(topic, message, cancellationToken);
      _logger.LogDebug(
        "Published {EventType} to {Topic} partition {Partition} offset {Offset}",
        typeof(TEvent).Name, result.Topic, result.Partition, result.Offset);
    }
    catch (ProduceException<string, string> ex)
    {
      _logger.LogError(ex, "Failed to publish {EventType} to topic {Topic}", typeof(TEvent).Name, topic);
      // Accept eventual consistency — do NOT rethrow. Add Outbox pattern for guaranteed delivery.
    }
  }
}
