using Hex.Scaffold.Domain.Common;

namespace Hex.Scaffold.Adapters.Persistence.Common;

public abstract class RepositoryBase<T>(DbContext dbContext) : IRepository<T>, IReadRepository<T>
  where T : class, IAggregateRoot
{
  protected DbContext DbContext { get; } = dbContext;

  public async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
  {
    var entry = await DbContext.Set<T>().AddAsync(entity, cancellationToken);
    await DbContext.SaveChangesAsync(cancellationToken);
    return entry.Entity;
  }

  public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
  {
    DbContext.Set<T>().Update(entity);
    await DbContext.SaveChangesAsync(cancellationToken);
  }

  public async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
  {
    DbContext.Set<T>().Remove(entity);
    await DbContext.SaveChangesAsync(cancellationToken);
  }

  public async Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) =>
    await DbContext.Set<T>().FindAsync([id], cancellationToken);

  public async Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
  {
    var query = DbContext.Set<T>().AsQueryable();

    if (specification.WhereExpression is not null)
      query = query.Where(specification.WhereExpression);

    return await query.FirstOrDefaultAsync(cancellationToken);
  }

  // Bulk lookup used by the Kafka inbound batch processor. The naive single-
  // key Find loop is intentional: EF tracks each loaded entity in the
  // ChangeTracker (so a follow-up SaveBatchAsync can mutate them in place
  // without re-attaching) and dedupes if duplicate ids are passed.
  // Going through Set<T>().Find keeps this generic over the aggregate's
  // PK type — no need for the caller (or this class) to know the column name.
  public async Task<IReadOnlyList<T>> GetByIdsAsync<TId>(
    IReadOnlyList<TId> ids, CancellationToken cancellationToken = default)
  {
    if (ids.Count == 0) return [];
    var loaded = new List<T>(ids.Count);
    foreach (var id in ids)
    {
      var entity = await DbContext.Set<T>().FindAsync([id], cancellationToken);
      if (entity is not null) loaded.Add(entity);
    }
    return loaded;
  }

  // Coalesced write: AddAsync / Update / Remove for every op, then a SINGLE
  // SaveChangesAsync round trip. The EventDispatcherInterceptor still fires
  // after SaveChanges and walks ChangeTracker.Entries<HasDomainEventsBase>()
  // — works for batches as-is, no extra wiring required.
  public async Task SaveBatchAsync(
    IReadOnlyList<BatchOp<T>> ops, CancellationToken cancellationToken = default)
  {
    if (ops.Count == 0) return;
    var set = DbContext.Set<T>();
    foreach (var op in ops)
    {
      switch (op.Kind)
      {
        case BatchOpKind.Add:
          await set.AddAsync(op.Entity, cancellationToken);
          break;
        case BatchOpKind.Update:
          set.Update(op.Entity);
          break;
        case BatchOpKind.Delete:
          set.Remove(op.Entity);
          break;
      }
    }
    await DbContext.SaveChangesAsync(cancellationToken);
  }
}
