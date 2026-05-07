namespace Hex.Scaffold.Application.Accounts.Batch;

// Adapter-agnostic shape of a single inbound Kafka message. The consumer
// delivers (event_type, raw json payload) — deserialisation, classification,
// idempotency, and aggregate construction live in the batch processor so the
// consumer stays "dumb" (poll → drain → call → commit). Carrying the raw
// JSON instead of a parsed DTO keeps the consumer free of any domain-shape
// dependency.
public sealed record AccountInboundEvent(string? EventType, string PayloadJson);

// Coalesced write port for the Kafka inbound path. One ProcessAsync call per
// consumer-poll batch:
//   1) deserialise + classify each event
//   2) bulk-load existing aggregates for the batch's id-set
//   3) apply the per-op aggregate operation in memory
//   4) flush every change in a SINGLE IRepository<Account>.SaveBatchAsync call
//
// REST inbound is unchanged — Mediator handlers (Create/Update/Delete) own
// the per-event flow that REST traffic still exercises.
public interface IAccountBatchProcessor
{
  Task ProcessAsync(IReadOnlyList<AccountInboundEvent> events, CancellationToken cancellationToken);
}
