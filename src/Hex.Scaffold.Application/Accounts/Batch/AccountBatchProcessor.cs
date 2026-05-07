using System.Text.Json;
using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.AccountAggregate.Events;

namespace Hex.Scaffold.Application.Accounts.Batch;

// Coalesces a Kafka consumer-poll batch of (event_type, raw json) tuples
// into a single IRepository<Account>.SaveBatchAsync round trip. The shape
// matches the existing per-event Mediator handlers so REST behaviour is
// preserved for the REST-inbound path:
//
//   Create  → idempotency-guarded Add (skip if already exists)
//   Update  → load + ApplyUpdate (drop if not found, mirroring REST 404)
//   Delete  → load + MarkDeleted (drop if not found, idempotent)
//
// Phase 5 v2 measured both repos handler-bound at ~110-200 events/s per pod
// with per-event SaveChanges. With BatchSize=200 + BatchLingerMs=50, this
// drops the per-message DB round trip from O(N) to O(1), unblocking the
// raised Gold/Platinum throughput targets (2.5k/5k events/s).
public sealed class AccountBatchProcessor(
  IRepository<Account> _repository,
  IMediator _mediator,
  ILogger<AccountBatchProcessor> _logger) : IAccountBatchProcessor
{
  // Snake-case JSON options mirroring the consumer's previous wire-shape
  // contract. PropertyNameCaseInsensitive keeps DTO binding tolerant of
  // payloads emitted by both the .NET outbound producer (PascalCase props
  // serialised as snake_case) and the k6 producer (snake_case literal).
  private static readonly JsonSerializerOptions _jsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
  };

  public async Task ProcessAsync(IReadOnlyList<AccountInboundEvent> events, CancellationToken cancellationToken)
  {
    if (events.Count == 0) return;

    // 1) Deserialise + classify. Anything that fails to parse is logged and
    //    dropped; a poison message must never wedge the whole batch.
    var creates = new List<(AccountId Id, KafkaAccountPayload Dto)>(events.Count);
    var updates = new List<(AccountId Id, KafkaAccountPayload Dto)>(events.Count);
    var deletes = new List<AccountId>(events.Count);

    foreach (var e in events)
    {
      KafkaAccountPayload? dto;
      try
      {
        dto = JsonSerializer.Deserialize<KafkaAccountPayload>(e.PayloadJson, _jsonOpts);
      }
      catch (JsonException ex)
      {
        _logger.LogError(ex, "Failed to deserialize message with key {EventType}", e.EventType ?? "(null)");
        continue;
      }
      if (dto is null) continue;

      switch (e.EventType)
      {
        case nameof(AccountCreatedEvent):
          if (dto.Id is { } cid) creates.Add((AccountId.From(cid), dto));
          else _logger.LogWarning("AccountCreatedEvent payload missing required 'id' field");
          break;
        case nameof(AccountUpdatedEvent):
          if (dto.Id is { } uid) updates.Add((AccountId.From(uid), dto));
          else _logger.LogWarning("AccountUpdatedEvent payload missing required 'id' field");
          break;
        case nameof(AccountDeletedEvent):
          if (dto.Id is { } did) deletes.Add(AccountId.From(did));
          else _logger.LogWarning("AccountDeletedEvent payload missing required 'id' field");
          break;
        default:
          _logger.LogWarning("Unknown event type: {EventType}", e.EventType ?? "(null)");
          break;
      }
    }

    // 2) Bulk-load existing aggregates the batch needs (idempotency on
    //    Create + read-before-write on Update/Delete). Distinct() folds
    //    the same-id-twice case (e.g. retried create + immediate update).
    var idsToLoad = creates.Select(c => c.Id)
      .Concat(updates.Select(u => u.Id))
      .Concat(deletes)
      .Distinct()
      .ToList();

    var loaded = await _repository.GetByIdsAsync<AccountId>(idsToLoad, cancellationToken);
    var byId = loaded.ToDictionary(a => a.Id, a => a);

    // 3) Build batch ops. Each branch mirrors the per-event Mediator handler
    //    semantics so REST and Kafka inbound stay behaviourally equivalent.
    var ops = new List<BatchOp<Account>>(events.Count);
    var createdEntities = new List<Account>();
    // Tracks ids whose Add op is already pending in this batch. Used to
    // collapse same-batch Create+Update / Create+Delete sequences for the
    // same id into a single op so the underlying repository never sees
    // Add+Update or Add+Delete on the same in-memory aggregate.
    //
    // Why this matters: in the EF/Postgres path, set.Update(entity) on an
    // entity currently in EntityState.Added flips its state to Modified —
    // SaveChanges then emits UPDATE for a row that has not been INSERTed
    // yet, which Postgres reports as "0 rows affected" and EF surfaces as
    // DbUpdateConcurrencyException. The Mongo path has the analogue: an
    // unordered BulkWrite of InsertOne + ReplaceOne for the same id can
    // race the Replace ahead of the Insert, dropping the update payload.
    // Collapsing to a single Add (which already carries the post-update
    // state — `account` is the same in-memory instance and ApplyUpdate
    // mutates it in place) avoids both failure modes cleanly.
    var pendingAddIds = new HashSet<AccountId>();

    foreach (var (id, dto) in creates)
    {
      // Idempotency for at-least-once delivery — a replayed Create for an
      // already-persisted aggregate is a no-op (matches CreateAccountHandler).
      if (byId.ContainsKey(id)) continue;

      var account = Account.CreateWithExternalId(
        id: id,
        livemode: dto.Livemode ?? false,
        displayName: dto.DisplayName.GetStringOrNull(),
        contactEmail: dto.ContactEmail.GetStringOrNull(),
        contactPhone: dto.ContactPhone.GetStringOrNull(),
        appliedConfigurations: ToAppliedConfigs(dto.AppliedConfigurations.AsStringList()),
        configurationJson: dto.Configuration.GetRawTextOrNull(),
        identityJson: dto.Identity.GetRawTextOrNull(),
        defaultsJson: dto.Defaults.GetRawTextOrNull(),
        metadataJson: dto.Metadata.GetRawTextOrNull());

      ops.Add(new BatchOp<Account>(BatchOpKind.Add, account));
      createdEntities.Add(account);
      // Track in byId so a subsequent Update/Delete for the same id in the
      // same batch sees the just-created aggregate.
      byId[id] = account;
      pendingAddIds.Add(id);
    }

    foreach (var (id, dto) in updates)
    {
      if (!byId.TryGetValue(id, out var account))
      {
        // Mirrors UpdateAccountHandler's Result<AccountDto>.NotFound — drop
        // the op without throwing so the batch keeps making progress.
        _logger.LogDebug("AccountUpdatedEvent for unknown account {AccountId} — skipping", id);
        continue;
      }

      account.ApplyUpdate(
        displayName: dto.DisplayName.ToMaybeString(),
        contactEmail: dto.ContactEmail.ToMaybeString(),
        contactPhone: dto.ContactPhone.ToMaybeString(),
        appliedConfigurations: ToMaybeAppliedConfigs(dto.AppliedConfigurations),
        configurationJson: dto.Configuration.ToMaybeRawJson(),
        identityJson: dto.Identity.ToMaybeRawJson(),
        defaultsJson: dto.Defaults.ToMaybeRawJson(),
        metadataJson: dto.Metadata.ToMaybeRawJson());

      // Same-batch Create + Update for the same id: mutation has already
      // landed on the in-memory aggregate that the pending Add op points
      // at, so the persisted INSERT will carry the post-update state.
      // Skipping the Update op avoids the EF Add→Modified state flip.
      if (pendingAddIds.Contains(id)) continue;

      ops.Add(new BatchOp<Account>(BatchOpKind.Update, account));
    }

    foreach (var id in deletes)
    {
      if (!byId.TryGetValue(id, out var account))
      {
        // Mirrors DeleteAccountHandler — unknown id is a successful no-op
        // (idempotent delete).
        _logger.LogDebug("AccountDeletedEvent for unknown account {AccountId} — treating as no-op", id);
        continue;
      }

      account.MarkDeleted();

      // Same-batch Create + Delete for the same id: cancel the pending Add
      // outright (net write = no-op). Issuing both ops would have EF emit
      // INSERT + DELETE for an aggregate that ultimately is not persisted
      // and Mongo BulkWrite a similar Insert + Delete pair.
      if (pendingAddIds.Remove(id))
      {
        var addIdx = ops.FindIndex(o => o.Kind == BatchOpKind.Add && ReferenceEquals(o.Entity, account));
        if (addIdx >= 0) ops.RemoveAt(addIdx);
        // Drop from createdEntities too so the post-write Mediator publish
        // does not announce a Create that never happened.
        createdEntities.Remove(account);
        continue;
      }

      ops.Add(new BatchOp<Account>(BatchOpKind.Delete, account));
    }

    if (ops.Count == 0) return;

    // 4) Single batched flush. EventDispatcherInterceptor (Postgres) /
    //    inline dispatcher call (Mongo) fires each aggregate's domain events
    //    after the write completes.
    await _repository.SaveBatchAsync(ops, cancellationToken);

    // CreateAccountHandler also re-publishes AccountCreatedEvent through
    // Mediator so any application-layer notification handlers (e.g. the
    // outbound publisher) see the create. Preserve that here for the
    // create-path so REST and Kafka inbound stay symmetric.
    foreach (var account in createdEntities)
    {
      await _mediator.Publish(new AccountCreatedEvent(account), cancellationToken);
    }

    _logger.LogDebug(
      "Batched {Count} ops (creates={Creates} updates={Updates} deletes={Deletes})",
      ops.Count, creates.Count, updates.Count, deletes.Count);
  }

  // Translate wire-format strings to the SmartEnum-shaped value list the
  // domain expects. Unknown values are dropped (the consumer is at-least-
  // once: throwing here would wedge the whole batch on a single bad payload).
  private static List<AppliedConfiguration> ToAppliedConfigs(IEnumerable<string>? raw)
  {
    if (raw is null) return [];
    var list = new List<AppliedConfiguration>();
    foreach (var value in raw)
    {
      var matched = AppliedConfiguration.List.FirstOrDefault(c => c.Value == value);
      if (matched is not null) list.Add(matched);
    }
    return list;
  }

  // Maybe-tuple variant for the partial-update path. Treats unknown values
  // the same as ToAppliedConfigs — drop and continue.
  private static (bool HasValue, IReadOnlyList<AppliedConfiguration>? Value) ToMaybeAppliedConfigs(JsonElement el)
  {
    if (el.ValueKind == JsonValueKind.Undefined) return (false, null);
    if (el.ValueKind == JsonValueKind.Null) return (true, null);
    if (el.ValueKind != JsonValueKind.Array) return (false, null);

    var list = new List<AppliedConfiguration>();
    foreach (var item in el.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.String) continue;
      var value = item.GetString();
      var matched = AppliedConfiguration.List.FirstOrDefault(c => c.Value == value);
      if (matched is not null) list.Add(matched);
    }
    return (true, list);
  }
}
