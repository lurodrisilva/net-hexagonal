namespace Hex.Scaffold.Domain.Common;

public interface IRepository<T> where T : class, IAggregateRoot
{
  Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
  Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
  Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
  Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default);
  Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
}
