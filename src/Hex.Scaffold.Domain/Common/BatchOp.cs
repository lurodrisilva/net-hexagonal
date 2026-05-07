namespace Hex.Scaffold.Domain.Common;

// Single-aggregate write operation kind for the bulk-flush port.
// Add/Update/Delete map 1:1 to the EF DbSet methods on the Postgres adapter
// and to InsertOne/ReplaceOne/DeleteOne MongoDB BulkWrite models on the
// Mongo adapter — no per-event SaveChanges or per-event single-doc round
// trip; the entire batch flushes in a single network call.
public enum BatchOpKind { Add, Update, Delete }

// Aggregate-typed batch op carried by IRepository<T>.SaveBatchAsync. Sealed
// record so the consumer-side coalescer can build a `List<BatchOp<Account>>`
// allocation-light and let the adapter dispatch by kind.
public sealed record BatchOp<T>(BatchOpKind Kind, T Entity)
  where T : class, IAggregateRoot;
