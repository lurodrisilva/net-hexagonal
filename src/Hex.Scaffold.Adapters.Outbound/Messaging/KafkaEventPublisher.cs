using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Hex.Scaffold.Domain.Common;
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
    // Producer span uses the OTel messaging semantic conventions so the
    // Application Map / Live Metrics show "publish <topic>" as a Kafka
    // dependency edge with the right destination/operation tags.
    using var activity = KafkaTelemetry.Source.StartActivity(
      $"publish {topic}", ActivityKind.Producer);
    activity?.SetTag("messaging.system", "kafka");
    activity?.SetTag("messaging.operation", "publish");
    activity?.SetTag("messaging.destination.name", topic);
    activity?.SetTag("messaging.kafka.message.key", typeof(TEvent).Name);

    try
    {
      var serialized = JsonSerializer.Serialize(@event);
      var message = new Message<string, string>
      {
        Key = typeof(TEvent).Name,
        Value = serialized,
        // Propagate W3C trace context to the consumer side so cross-service
        // edges land on the App Map. Mirrors what HttpClient instrumentation
        // does automatically for HTTP — for Kafka we have to do it by hand.
        Headers = BuildTracingHeaders(activity),
      };

      var result = await _producer.ProduceAsync(topic, message, cancellationToken);
      activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
      activity?.SetTag("messaging.kafka.offset", result.Offset.Value);

      _logger.LogDebug(
        "Published {EventType} to {Topic} partition {Partition} offset {Offset}",
        typeof(TEvent).Name, result.Topic, result.Partition, result.Offset);
    }
    catch (ProduceException<string, string> ex)
    {
      activity?.SetStatus(ActivityStatusCode.Error, ex.Error.Reason);
      _logger.LogError(ex, "Failed to publish {EventType} to topic {Topic}", typeof(TEvent).Name, topic);
      // Accept eventual consistency — do NOT rethrow. Add Outbox pattern for guaranteed delivery.
    }
  }

  private static Headers? BuildTracingHeaders(Activity? activity)
  {
    if (activity is null) return null;

    var headers = new Headers
    {
      { "traceparent", Encoding.UTF8.GetBytes(activity.Id ?? string.Empty) }
    };
    if (!string.IsNullOrEmpty(activity.TraceStateString))
      headers.Add("tracestate", Encoding.UTF8.GetBytes(activity.TraceStateString));
    return headers;
  }
}
