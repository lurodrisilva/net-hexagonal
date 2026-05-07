namespace Hex.Scaffold.Domain.Common;

public interface IRepository<T> where T : class, IAggregateRoot
{
  Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
  Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
  Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
  Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default);
  Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

  // Bulk-load existing aggregates by id. Used by the Kafka inbound batch
  // processor to fold "is this a duplicate?" / "does the target row exist?"
  // checks for an entire poll-batch into a single round trip before the
  // adapter flushes the writes.
  Task<IReadOnlyList<T>> GetByIdsAsync<TId>(
    IReadOnlyList<TId> ids, CancellationToken cancellationToken = default);

  // Coalesced write: dispatch every op in `ops` and flush in ONE round trip
  // (EF SaveChangesAsync on Postgres, BulkWriteAsync on Mongo). Domain events
  // still fire per aggregate — Postgres goes through EventDispatcherInterceptor
  // after SaveChanges, Mongo dispatches inline after BulkWrite.
  Task SaveBatchAsync(
    IReadOnlyList<BatchOp<T>> ops, CancellationToken cancellationToken = default);
}
