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
}
