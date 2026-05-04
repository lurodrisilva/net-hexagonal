using Hex.Scaffold.Domain.AccountAggregate;
using MongoDB.Driver;

namespace Hex.Scaffold.Adapters.Persistence.MongoDb;

// Mongo-backed Account repository. Mirrors the Postgres EfRepository surface
// (IRepository<Account> + IReadRepository<Account>) so handler code is
// adapter-agnostic. Domain-event flush is invoked inline after each write
// to keep parity with the Postgres EventDispatcherInterceptor (which fires
// after SaveChanges) — there's no equivalent transactional hook in Mongo
// vCore, so we accept "events fire on success" semantics.
public sealed class MongoAccountRepository(
  IMongoCollection<Account> _collection,
  IDomainEventDispatcher _dispatcher) : IRepository<Account>, IReadRepository<Account>
{
  public async Task<Account> AddAsync(Account entity, CancellationToken cancellationToken = default)
  {
    await _collection.InsertOneAsync(entity, options: null, cancellationToken);
    await _dispatcher.DispatchAndClearEvents([entity]);
    return entity;
  }

  public async Task UpdateAsync(Account entity, CancellationToken cancellationToken = default)
  {
    var filter = Builders<Account>.Filter.Eq(a => a.Id, entity.Id);
    await _collection.ReplaceOneAsync(filter, entity, new ReplaceOptions { IsUpsert = false }, cancellationToken);
    await _dispatcher.DispatchAndClearEvents([entity]);
  }

  public async Task DeleteAsync(Account entity, CancellationToken cancellationToken = default)
  {
    var filter = Builders<Account>.Filter.Eq(a => a.Id, entity.Id);
    await _collection.DeleteOneAsync(filter, cancellationToken);
    await _dispatcher.DispatchAndClearEvents([entity]);
  }

  public async Task<Account?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
  {
    if (id is not AccountId accountId)
    {
      throw new ArgumentException(
        $"MongoAccountRepository expects AccountId, got {typeof(TId).Name}.", nameof(id));
    }

    var filter = Builders<Account>.Filter.Eq(a => a.Id, accountId);
    return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
  }

  public async Task<Account?> FirstOrDefaultAsync(
    ISpecification<Account> specification, CancellationToken cancellationToken = default)
  {
    var filter = specification.WhereExpression is null
      ? Builders<Account>.Filter.Empty
      : Builders<Account>.Filter.Where(specification.WhereExpression);
    return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
  }
}
