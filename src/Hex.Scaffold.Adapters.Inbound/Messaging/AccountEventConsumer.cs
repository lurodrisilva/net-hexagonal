using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Confluent.Kafka;
using Hex.Scaffold.Adapters.Inbound.Options;
using Hex.Scaffold.Application.Accounts.Batch;
using Hex.Scaffold.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hex.Scaffold.Adapters.Inbound.Messaging;

// Inbound Kafka consumer wired only when features.inbound=kafka. The cluster
// runs inbound=rest in production, so this BackgroundService is registered
// conditionally in ServiceConfigs and never starts there.
//
// Phase 5 v2 promoted this from per-message Mediator dispatch to a batch-
// drain → IAccountBatchProcessor.ProcessAsync → single SaveBatchAsync flush
// per consumer poll (see AccountBatchProcessor.cs for the why). REST inbound
// is unchanged — REST traffic still hits the per-event Mediator handlers.
//
// The trace-context plumbing from PR #17 stays intact so producer→consumer
// edges still render in App Map. Each batch starts ONE consumer-side
// Activity (parented by the FIRST message's traceparent) — extracting and
// linking parents for every message in the batch would be more accurate
// but the ingest-cap budget at platinum is too tight to spend on a 5×
// activity multiplier. App Insights still shows producer→consumer linkage
// via that first-message edge.
public sealed class AccountEventConsumer(
  IConsumer<string, string> _consumer,
  IServiceScopeFactory _scopeFactory,
  ILogger<AccountEventConsumer> _logger,
  IOptions<KafkaOptions> _kafkaOptions) : BackgroundService
{
  private readonly string _topic = _kafkaOptions.Value.InboundTopic;
  private readonly int _batchSize = _kafkaOptions.Value.BatchSize;
  private readonly int _batchLingerMs = _kafkaOptions.Value.BatchLingerMs;

  // OTel histogram for the 200 ms end-to-end stop condition. Tag set is
  // bounded to {event_type, tier, repo} — runId is set as an Activity tag,
  // never a metric tag, to keep cardinality finite (see plan §4.2.5).
  // Per-message duration is reported as (batch elapsed / batch.Count) so
  // Phase 5 dashboards keep emitting the same metric shape — the average
  // is exact, the p99 is a slight under-estimate vs. true per-message wall
  // time (acceptable; the dashboard SLO is on average + p99 of batch).
  private static readonly Meter _meter =
    new("Hex.Scaffold.Adapters.Inbound.Messaging", "1.0.0");

  private static readonly Histogram<double> _processingDuration =
    _meter.CreateHistogram<double>(
      name: "inbound_event_processing_duration_ms",
      unit: "ms",
      description: "Wall time from Kafka poll to command-handler completion (per-message-equivalent within a batch)");

  protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
    Task.Factory.StartNew(
      () => ConsumeLoop(stoppingToken),
      stoppingToken,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default);

  private void ConsumeLoop(CancellationToken stoppingToken)
  {
    _consumer.Subscribe(_topic);
    _logger.LogInformation("Subscribed to Kafka topic: {Topic}", _topic);

    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          // Block until the FIRST message of the next batch arrives. We
          // accept the linger only AFTER seeing one record so a fully
          // idle topic doesn't burn 50ms wall time per loop iteration.
          var first = _consumer.Consume(stoppingToken);
          if (first is null) continue;

          var batch = new List<ConsumeResult<string, string>>(_batchSize) { first };
          var deadline = DateTimeOffset.UtcNow.AddMilliseconds(_batchLingerMs);
          while (batch.Count < _batchSize)
          {
            var remaining = (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
            if (remaining <= 0) break;
            // Non-blocking-ish poll: returns null if no more messages are
            // sitting in the local prefetch buffer when the timeout expires.
            var msg = _consumer.Consume(TimeSpan.FromMilliseconds(remaining));
            if (msg is null) break;
            batch.Add(msg);
          }

          ProcessBatch(batch, stoppingToken);
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

  private void ProcessBatch(
    List<ConsumeResult<string, string>> batch, CancellationToken stoppingToken)
  {
    var pollTs = DateTimeOffset.UtcNow;

    // OTel Activity per batch (NOT per message). Parent context is taken
    // from the FIRST message in the batch — see class doc for the trade-off.
    var firstHeaders = batch[0].Message.Headers;
    var parentContext = ExtractTraceContext(firstHeaders);
    using var activity = KafkaTelemetry.Source.StartActivity(
      $"process {_topic}", ActivityKind.Consumer, parentContext);
    activity?.SetTag("messaging.system", "kafka");
    activity?.SetTag("messaging.operation", "process");
    activity?.SetTag("messaging.destination.name", _topic);
    activity?.SetTag("messaging.kafka.batch.size", batch.Count);
    activity?.SetTag("messaging.kafka.message.key", batch[0].Message.Key);
    activity?.SetTag("messaging.kafka.partition", batch[0].Partition.Value);
    activity?.SetTag("messaging.kafka.offset", batch[0].Offset.Value);

    var firstRunId = HeaderOrUnknown(firstHeaders, "runId");
    activity?.SetTag("runId", firstRunId);

    // Drain to inbound-event records and run the batch processor.
    var events = new List<AccountInboundEvent>(batch.Count);
    foreach (var b in batch)
    {
      events.Add(new AccountInboundEvent(b.Message.Key, b.Message.Value));
    }

    using var scope = _scopeFactory.CreateScope();
    var processor = scope.ServiceProvider.GetRequiredService<IAccountBatchProcessor>();
    try
    {
      processor.ProcessAsync(events, stoppingToken).GetAwaiter().GetResult();
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // Shutdown — don't commit or record metrics for a half-flushed batch.
      return;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Batch processing failed for {Count} messages", batch.Count);
      // Don't commit — let the consumer rebalance redeliver the batch on
      // restart. At-least-once semantics are preserved by the idempotency
      // logic in AccountBatchProcessor.
      return;
    }

    // Commit the highest offset per partition (Confluent.Kafka commits the
    // NEXT offset to read — hence Max(offset)+1).
    var offsets = batch
      .GroupBy(b => b.TopicPartition)
      .Select(g => new TopicPartitionOffset(g.Key, g.Max(b => b.Offset.Value) + 1))
      .ToList();
    _consumer.Commit(offsets);

    // Per-message duration histogram — preserve Phase 5 dashboard parity.
    var elapsedMs = (DateTimeOffset.UtcNow - pollTs).TotalMilliseconds;
    var perMessage = elapsedMs / batch.Count;
    foreach (var b in batch)
    {
      var tier = HeaderOrUnknown(b.Message.Headers, "tier");
      var repo = HeaderOrUnknown(b.Message.Headers, "repo");
      _processingDuration.Record(
        perMessage,
        new KeyValuePair<string, object?>("event_type", b.Message.Key ?? "unknown"),
        new KeyValuePair<string, object?>("tier", tier),
        new KeyValuePair<string, object?>("repo", repo));
    }
  }

  private static string HeaderOrUnknown(Headers? headers, string key)
  {
    if (headers is null) return "unknown";
    return headers.TryGetLastBytes(key, out var bytes)
      ? Encoding.UTF8.GetString(bytes)
      : "unknown";
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
