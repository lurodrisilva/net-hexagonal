using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Hex.Scaffold.Domain.AccountAggregate.Events;
using Hex.Scaffold.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hex.Scaffold.Adapters.Inbound.Messaging;

// Inbound Kafka consumer wired only when features.inbound=kafka. The cluster
// runs inbound=rest, so this BackgroundService is registered conditionally
// in ServiceConfigs and never starts in production. Kept compiling against
// the Account aggregate's events so the kafka path stays viable for the
// `inbound=kafka` selector — but minimal: log + commit, no read-model
// upsert (the Sample-era SampleReadModelRepository was retired alongside
// the Sample aggregate; an AccountReadModelRepository can drop in here when
// that shape is needed).
//
// The trace-context plumbing from PR #17 stays intact so producer→consumer
// edges still render in App Map when this path is exercised.
public sealed class AccountEventConsumer(
  IConsumer<string, string> _consumer,
  IServiceScopeFactory _scopeFactory,
  ILogger<AccountEventConsumer> _logger) : BackgroundService
{
  // Mirrors the topic name used by AccountEventPublishHandler.
  private const string Topic = "v2.core.accounts";

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
          ProcessMessage(message);

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

  private void ProcessMessage(ConsumeResult<string, string> message)
  {
    var eventType = message.Message.Key;
    try
    {
      var payload = JsonSerializer.Deserialize<JsonElement>(message.Message.Value);
      var accountId = TryReadAccountId(payload);

      switch (eventType)
      {
        case nameof(AccountCreatedEvent):
          _logger.LogInformation("Consumed v2.core.account.created for {AccountId}", accountId);
          break;
        case nameof(AccountUpdatedEvent):
          _logger.LogInformation("Consumed v2.core.account.updated for {AccountId}", accountId);
          break;
        default:
          _logger.LogWarning("Unknown event type: {EventType}", eventType);
          break;
      }
    }
    catch (JsonException ex)
    {
      _logger.LogError(ex, "Failed to deserialize message with key {EventType}", eventType);
    }
  }

  private static string? TryReadAccountId(JsonElement payload)
  {
    // Tolerant of both serialization shapes — Vogen's SystemTextJson
    // converter on AccountId emits a plain string; without it, it would
    // emit an object {"Value": "acct_…"}.
    if (payload.TryGetProperty("Account", out var account))
    {
      if (account.TryGetProperty("Id", out var id))
      {
        return id.ValueKind switch
        {
          JsonValueKind.String => id.GetString(),
          JsonValueKind.Object when id.TryGetProperty("Value", out var inner) => inner.GetString(),
          _ => null
        };
      }
    }
    return null;
  }

  private static ActivityContext ExtractTraceContext(Headers? headers)
  {
    if (headers is null) return default;
    if (!headers.TryGetLastBytes("traceparent", out var traceparentBytes)) return default;
    var traceparent = Encoding.UTF8.GetString(traceparentBytes);
    var tracestate = headers.TryGetLastBytes("tracestate", out var tracestateBytes)
      ? Encoding.UTF8.GetString(tracestateBytes)
      : null;
    return ActivityContext.TryParse(traceparent, tracestate, out var ctx) ? ctx : default;
  }
}
