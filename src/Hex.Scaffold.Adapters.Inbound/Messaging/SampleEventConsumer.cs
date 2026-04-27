using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Hex.Scaffold.Domain.Common;
using Hex.Scaffold.Domain.Ports.Outbound;
using Hex.Scaffold.Domain.SampleAggregate.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hex.Scaffold.Adapters.Inbound.Messaging;

public sealed class SampleEventConsumer(
  IConsumer<string, string> _consumer,
  IServiceScopeFactory _scopeFactory,
  ILogger<SampleEventConsumer> _logger) : BackgroundService
{
  private const string Topic = "sample-events";

  protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
    Task.Factory.StartNew(
      () => ConsumeLoop(stoppingToken),
      stoppingToken,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default);

  private void ConsumeLoop(CancellationToken stoppingToken)
  {
    _consumer.Subscribe(Topic);
    _logger.LogInformation("Subscribed to Kafka topic: {Topic}", Topic);

    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          var message = _consumer.Consume(stoppingToken);
          if (message is null) continue;

          // Extract the W3C trace context propagated by KafkaEventPublisher
          // and link this consumer span to the producer trace, so the App Map
          // draws the producer→consumer edge across services.
          var parentContext = ExtractTraceContext(message.Message.Headers);
          using var activity = KafkaTelemetry.Source.StartActivity(
            $"process {Topic}", ActivityKind.Consumer, parentContext);
          activity?.SetTag("messaging.system", "kafka");
          activity?.SetTag("messaging.operation", "process");
          activity?.SetTag("messaging.destination.name", Topic);
          activity?.SetTag("messaging.kafka.message.key", message.Message.Key);
          activity?.SetTag("messaging.kafka.partition", message.Partition.Value);
          activity?.SetTag("messaging.kafka.offset", message.Offset.Value);

          using var scope = _scopeFactory.CreateScope();
          ProcessMessageAsync(message, scope.ServiceProvider, stoppingToken)
            .GetAwaiter().GetResult();

          _consumer.Commit(message);
        }
        catch (ConsumeException ex)
        {
          _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
          break;
        }
      }
    }
    finally
    {
      _consumer.Close();
    }
  }

  private async Task ProcessMessageAsync(
    ConsumeResult<string, string> message,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken)
  {
    var repository = serviceProvider.GetRequiredService<ISampleReadModelRepository>();
    var eventType = message.Message.Key;

    try
    {
      var payload = JsonSerializer.Deserialize<JsonElement>(message.Message.Value);

      switch (eventType)
      {
        case nameof(SampleCreatedEvent):
        {
          var sample = payload.GetProperty("Sample");
          await repository.UpsertAsync(new SampleReadModel
          {
            SampleId = sample.GetProperty("Id").GetProperty("Value").GetInt32(),
            // SampleName has SystemTextJson converter → serialized as plain string
            Name = sample.GetProperty("Name").GetString() ?? string.Empty,
            Status = sample.GetProperty("Status").GetProperty("Name").GetString() ?? string.Empty,
            Description = sample.TryGetProperty("Description", out var createdDesc)
              ? createdDesc.GetString()
              : null,
            LastUpdated = DateTime.UtcNow
          }, cancellationToken);
          break;
        }
        case nameof(SampleUpdatedEvent):
        {
          var sample = payload.GetProperty("Sample");
          await repository.UpsertAsync(new SampleReadModel
          {
            SampleId = sample.GetProperty("Id").GetProperty("Value").GetInt32(),
            Name = sample.GetProperty("Name").GetString() ?? string.Empty,
            Status = sample.GetProperty("Status").GetProperty("Name").GetString() ?? string.Empty,
            Description = sample.TryGetProperty("Description", out var updatedDesc)
              ? updatedDesc.GetString()
              : null,
            LastUpdated = DateTime.UtcNow
          }, cancellationToken);
          break;
        }
        case nameof(SampleDeletedEvent):
        {
          // SampleId has no SystemTextJson converter → serialized as {"Value": N}
          var sampleId = payload.GetProperty("SampleId").GetProperty("Value").GetInt32();
          await repository.DeleteAsync(sampleId, cancellationToken);
          break;
        }
        default:
          _logger.LogWarning("Unknown event type: {EventType}", eventType);
          break;
      }
    }
    catch (JsonException ex)
    {
      // TODO: Implement dead-letter topic for failed messages
      _logger.LogError(ex, "Failed to deserialize message with key {EventType}", eventType);
    }
    catch (KeyNotFoundException ex)
    {
      _logger.LogError(ex, "Missing expected property in event payload for key {EventType}", eventType);
    }
  }

  // W3C trace-context extraction — the inverse of the producer's header
  // injection in KafkaEventPublisher.BuildTracingHeaders. Returns
  // ActivityContext.None when traceparent is missing or unparseable, which
  // makes the consumer span a new root rather than failing the message.
  private static ActivityContext ExtractTraceContext(Headers? headers)
  {
    if (headers is null) return default;

    if (!headers.TryGetLastBytes("traceparent", out var traceparentBytes))
      return default;

    var traceparent = Encoding.UTF8.GetString(traceparentBytes);
    var tracestate = headers.TryGetLastBytes("tracestate", out var tracestateBytes)
      ? Encoding.UTF8.GetString(tracestateBytes)
      : null;

    return ActivityContext.TryParse(traceparent, tracestate, out var ctx) ? ctx : default;
  }
}
