using Hex.Scaffold.Application.Accounts;
using Hex.Scaffold.Application.Accounts.List;
using Hex.Scaffold.Domain.AccountAggregate;
using MongoDB.Driver;

namespace Hex.Scaffold.Adapters.Persistence.MongoDb;

// Mongo equivalent of PostgreSql.Queries.ListAccountsQueryService. Same
// over-fetch-by-one trick to detect HasMore without a second round-trip,
// same starting_after/ending_before mutual-exclusion guard, same paging
// key (Created descending). The only adapter-specific difference is that
// the query is built with the Mongo Filter/Sort fluent builder rather
// than EF Linq — semantics are identical.
public sealed class MongoListAccountsQueryService(IMongoCollection<Account> _collection)
  : IListAccountsQueryService
{
  public async Task<(IReadOnlyList<AccountDto> Items, bool HasMore)> ListAsync(
    int limit,
    string? startingAfter,
    string? endingBefore,
    CancellationToken cancellationToken = default)
  {
    if (startingAfter is not null && endingBefore is not null)
    {
      throw new ArgumentException("starting_after and ending_before are mutually exclusive.");
    }

    var fb = Builders<Account>.Filter;
    var filter = fb.Empty;

    if (startingAfter is not null)
    {
      var cursor = await ResolveCursorAsync(startingAfter, cancellationToken);
      if (cursor is null) return (Array.Empty<AccountDto>(), false);
      filter = fb.Lt(a => a.Created, cursor.Value);
    }
    else if (endingBefore is not null)
    {
      var cursor = await ResolveCursorAsync(endingBefore, cancellationToken);
      if (cursor is null) return (Array.Empty<AccountDto>(), false);
      filter = fb.Gt(a => a.Created, cursor.Value);
    }

    var fetch = limit + 1;
    var sort = endingBefore is not null
      ? Builders<Account>.Sort.Ascending(a => a.Created)
      : Builders<Account>.Sort.Descending(a => a.Created);

    var rows = await _collection
      .Find(filter)
      .Sort(sort)
      .Limit(fetch)
      .ToListAsync(cancellationToken);

    if (endingBefore is not null) rows.Reverse();

    var hasMore = rows.Count > limit;
    var page = hasMore ? rows.Take(limit) : rows;
    return (page.Select(AccountDto.FromAggregate).ToList(), hasMore);
  }

  private async Task<DateTime?> ResolveCursorAsync(string id, CancellationToken cancellationToken)
  {
    var typedId = AccountId.From(id);
    var doc = await _collection
      .Find(Builders<Account>.Filter.Eq(a => a.Id, typedId))
      .Project(a => (DateTime?)a.Created)
      .FirstOrDefaultAsync(cancellationToken);
    return doc;
  }
}
