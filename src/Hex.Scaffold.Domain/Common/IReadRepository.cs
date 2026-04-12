namespace Hex.Scaffold.Domain.Common;

public interface IReadRepository<T> where T : class, IAggregateRoot
{
  Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default);
  Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
}
