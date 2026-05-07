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

  // Bulk lookup used by the Kafka inbound batch processor. Single $in query
  // replaces N round-tripped FirstOrDefaultAsync calls.
  public async Task<IReadOnlyList<Account>> GetByIdsAsync<TId>(
    IReadOnlyList<TId> ids, CancellationToken cancellationToken = default)
  {
    if (ids.Count == 0) return [];
    var typed = new List<AccountId>(ids.Count);
    foreach (var id in ids)
    {
      if (id is not AccountId accountId)
      {
        throw new ArgumentException(
          $"MongoAccountRepository expects AccountId, got {typeof(TId).Name}.", nameof(ids));
      }
      typed.Add(accountId);
    }
    var filter = Builders<Account>.Filter.In(a => a.Id, typed);
    return await _collection.Find(filter).ToListAsync(cancellationToken);
  }

  // Coalesced write: build one WriteModel per op, fire BulkWriteAsync ONCE
  // (IsOrdered=false so the server can parallelise across the batch), then
  // dispatch domain events from every aggregate that participated. Mirrors
  // the per-op .NET method choice on the single-entity path:
  //   Add    → InsertOneModel
  //   Update → ReplaceOneModel (IsUpsert=false — matches the existing
  //            ReplaceOneAsync semantics, partial-update is already applied
  //            in the aggregate before this is called)
  //   Delete → DeleteOneModel
  public async Task SaveBatchAsync(
    IReadOnlyList<BatchOp<Account>> ops, CancellationToken cancellationToken = default)
  {
    if (ops.Count == 0) return;
    var models = new List<WriteModel<Account>>(ops.Count);
    foreach (var op in ops)
    {
      var filter = Builders<Account>.Filter.Eq(a => a.Id, op.Entity.Id);
      models.Add(op.Kind switch
      {
        BatchOpKind.Add => new InsertOneModel<Account>(op.Entity),
        BatchOpKind.Update => new ReplaceOneModel<Account>(filter, op.Entity) { IsUpsert = false },
        BatchOpKind.Delete => new DeleteOneModel<Account>(filter),
        _ => throw new InvalidOperationException($"Unknown BatchOpKind: {op.Kind}")
      });
    }
    await _collection.BulkWriteAsync(
      models, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
    await _dispatcher.DispatchAndClearEvents(ops.Select(o => o.Entity).Distinct());
  }
}
