using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Hex.Scaffold.Adapters.Inbound.Api.Accounts;
using Hex.Scaffold.Adapters.Inbound.Options;
using Hex.Scaffold.Application.Accounts.Create;
using Hex.Scaffold.Application.Accounts.Delete;
using Hex.Scaffold.Application.Accounts.Update;
using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.AccountAggregate.Events;
using Hex.Scaffold.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hex.Scaffold.Adapters.Inbound.Messaging;

// Inbound Kafka consumer wired only when features.inbound=kafka. The cluster
// runs inbound=rest in production, so this BackgroundService is registered
// conditionally in ServiceConfigs and never starts there.
//
// For Kafka loadtest runs (see .omc/plans/kafka-loadtest-plan.md §3.2),
// every consumed message is dispatched through the same Mediator command
// path the REST inbound endpoints use — so the persistence write is
// measured exactly the way production HTTP traffic measures it. Idempotency
// for at-least-once delivery is handled in CreateAccountHandler (§3.3) via
// Account.CreateWithExternalId (§3.0).
//
// The trace-context plumbing from PR #17 stays intact so producer→consumer
// edges still render in App Map.
public sealed class AccountEventConsumer(
  IConsumer<string, string> _consumer,
  IServiceScopeFactory _scopeFactory,
  ILogger<AccountEventConsumer> _logger,
  IOptions<KafkaOptions> _kafkaOptions) : BackgroundService
{
  private readonly string _topic = _kafkaOptions.Value.InboundTopic;

  // OTel histogram for the 200 ms end-to-end stop condition. Tag set is
  // bounded to {event_type, tier, repo} — runId is set as an Activity tag,
  // never a metric tag, to keep cardinality finite (see plan §4.2.5).
  private static readonly Meter _meter =
    new("Hex.Scaffold.Adapters.Inbound.Messaging", "1.0.0");

  private static readonly Histogram<double> _processingDuration =
    _meter.CreateHistogram<double>(
      name: "inbound_event_processing_duration_ms",
      unit: "ms",
      description: "Wall time from Kafka poll to command-handler completion");

  // Snake-case JSON options mirroring FastEndpoints' MiddlewareConfig so
  // the deserialiser sees the same wire shape the REST inbound endpoints
  // do. Vogen value objects (AccountId) use their built-in SystemTextJson
  // converter — no extra registrations required.
  private static readonly JsonSerializerOptions _jsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
  };

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
          var message = _consumer.Consume(stoppingToken);
          if (message is null) continue;

          var pollTs = DateTimeOffset.UtcNow;
          var parentContext = ExtractTraceContext(message.Message.Headers);
          using var activity = KafkaTelemetry.Source.StartActivity(
            $"process {_topic}", ActivityKind.Consumer, parentContext);
          activity?.SetTag("messaging.system", "kafka");
          activity?.SetTag("messaging.operation", "process");
          activity?.SetTag("messaging.destination.name", _topic);
          activity?.SetTag("messaging.kafka.message.key", message.Message.Key);
          activity?.SetTag("messaging.kafka.partition", message.Partition.Value);
          activity?.SetTag("messaging.kafka.offset", message.Offset.Value);

          var tier = HeaderOrUnknown(message.Message.Headers, "tier");
          var repo = HeaderOrUnknown(message.Message.Headers, "repo");
          var runId = HeaderOrUnknown(message.Message.Headers, "runId");
          activity?.SetTag("runId", runId);

          using var scope = _scopeFactory.CreateScope();
          var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

          ProcessMessage(message, mediator, pollTs, tier, repo).GetAwaiter().GetResult();

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

  private async Task ProcessMessage(
    ConsumeResult<string, string> message,
    IMediator mediator,
    DateTimeOffset pollTs,
    string tier,
    string repo)
  {
    var eventType = message.Message.Key;
    try
    {
      var result = eventType switch
      {
        nameof(AccountCreatedEvent) => await DispatchCreate(message.Message.Value, mediator),
        nameof(AccountUpdatedEvent) => await DispatchUpdate(message.Message.Value, mediator),
        nameof(AccountDeletedEvent) => await DispatchDelete(message.Message.Value, mediator),
        _ => HandleUnknown(eventType)
      };

      if (!result.IsSuccess)
      {
        _logger.LogWarning(
          "Event dispatch failed for {EventType}: {Error}",
          eventType,
          result.Errors.FirstOrDefault() ?? "(no error message)");
      }
    }
    catch (JsonException ex)
    {
      _logger.LogError(ex, "Failed to deserialize message with key {EventType}", eventType);
    }
    finally
    {
      var elapsedMs = (DateTimeOffset.UtcNow - pollTs).TotalMilliseconds;
      _processingDuration.Record(
        elapsedMs,
        new KeyValuePair<string, object?>("event_type", eventType ?? "unknown"),
        new KeyValuePair<string, object?>("tier", tier),
        new KeyValuePair<string, object?>("repo", repo));
    }
  }

  private async Task<Result> DispatchCreate(string json, IMediator mediator)
  {
    var dto = JsonSerializer.Deserialize<KafkaAccountPayload>(json, _jsonOpts)
      ?? throw new JsonException("AccountCreatedEvent payload deserialised to null");

    var cmd = new CreateAccountCommand(
      Livemode: dto.Livemode ?? false,
      DisplayName: dto.DisplayName.GetStringOrNull(),
      ContactEmail: dto.ContactEmail.GetStringOrNull(),
      ContactPhone: dto.ContactPhone.GetStringOrNull(),
      AppliedConfigurations: AccountFieldHelpers.ToAppliedConfigs(dto.AppliedConfigurations.AsStringList()),
      ConfigurationJson: dto.Configuration.GetRawTextOrNull(),
      IdentityJson: dto.Identity.GetRawTextOrNull(),
      DefaultsJson: dto.Defaults.GetRawTextOrNull(),
      MetadataJson: dto.Metadata.GetRawTextOrNull(),
      Id: dto.Id is { } idStr ? AccountId.From(idStr) : null);

    var createResult = await mediator.Send(cmd);
    return createResult.IsSuccess
      ? Result.Success()
      : Result.Error(createResult.Errors.FirstOrDefault() ?? "create failed");
  }

  private async Task<Result> DispatchUpdate(string json, IMediator mediator)
  {
    var dto = JsonSerializer.Deserialize<KafkaAccountPayload>(json, _jsonOpts)
      ?? throw new JsonException("AccountUpdatedEvent payload deserialised to null");

    if (dto.Id is null)
      return Result.Error("AccountUpdatedEvent payload missing required 'id' field");

    var cmd = new UpdateAccountCommand(
      Id: AccountId.From(dto.Id),
      DisplayName: dto.DisplayName.ToMaybeString(),
      ContactEmail: dto.ContactEmail.ToMaybeString(),
      ContactPhone: dto.ContactPhone.ToMaybeString(),
      AppliedConfigurations: dto.AppliedConfigurations.ToMaybeAppliedConfigs(),
      ConfigurationJson: dto.Configuration.ToMaybeRawJson(),
      IdentityJson: dto.Identity.ToMaybeRawJson(),
      DefaultsJson: dto.Defaults.ToMaybeRawJson(),
      MetadataJson: dto.Metadata.ToMaybeRawJson());

    var updateResult = await mediator.Send(cmd);
    return updateResult.IsSuccess
      ? Result.Success()
      : Result.Error(updateResult.Errors.FirstOrDefault() ?? "update failed");
  }

  private async Task<Result> DispatchDelete(string json, IMediator mediator)
  {
    var dto = JsonSerializer.Deserialize<KafkaAccountPayload>(json, _jsonOpts)
      ?? throw new JsonException("AccountDeletedEvent payload deserialised to null");

    if (dto.Id is null)
      return Result.Error("AccountDeletedEvent payload missing required 'id' field");

    return await mediator.Send(new DeleteAccountCommand(AccountId.From(dto.Id)));
  }


  private Result HandleUnknown(string? eventType)
  {
    _logger.LogWarning("Unknown event type: {EventType}", eventType ?? "(null)");
    return Result.Success();
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

// DTO mirroring the snake_case payload the k6 producer emits and the
// outbound producer's serialised AccountCreatedEvent/AccountUpdatedEvent
// shape. Every partial-update-capable field is JsonElement so
// (Undefined = absent, Null = explicit clear, value = set) survives the
// REST-equivalent ApplyUpdate path. Plain Id / Livemode are scalars that
// don't participate in partial-update semantics.
internal sealed class KafkaAccountPayload
{
  public string? Id { get; set; }
  public bool? Livemode { get; set; }
  public JsonElement DisplayName { get; set; }
  public JsonElement ContactEmail { get; set; }
  public JsonElement ContactPhone { get; set; }
  public JsonElement AppliedConfigurations { get; set; }
  public JsonElement Configuration { get; set; }
  public JsonElement Identity { get; set; }
  public JsonElement Defaults { get; set; }
  public JsonElement Metadata { get; set; }
}

internal static class JsonElementExtensions
{
  public static string? GetStringOrNull(this JsonElement el) =>
    el.ValueKind == JsonValueKind.String ? el.GetString() : null;

  public static string? GetRawTextOrNull(this JsonElement el) =>
    el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : el.GetRawText();

  public static List<string>? AsStringList(this JsonElement el)
  {
    if (el.ValueKind != JsonValueKind.Array) return null;
    var list = new List<string>();
    foreach (var item in el.EnumerateArray())
    {
      if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s) list.Add(s);
    }
    return list;
  }
}
