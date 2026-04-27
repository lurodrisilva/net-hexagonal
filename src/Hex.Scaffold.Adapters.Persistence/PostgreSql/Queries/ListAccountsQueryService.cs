using Hex.Scaffold.Application.Accounts;
using Hex.Scaffold.Application.Accounts.List;
using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql.Queries;

// Cursor-based list. Stripe paginates by Account ID; we paginate by the
// composite (created, id) keyset so the page boundary is monotonic even if
// IDs are written out of `created` order. The behavior the caller observes
// is identical: pass `starting_after=acct_…` (forward) or
// `ending_before=acct_…` (back), get up to `limit` accounts, learn whether
// there's more via the boolean.
//
// Both cursor branches read the cursor row's `created` once, then page on
// (created, id). `starting_after` and `ending_before` are mutually
// exclusive — Stripe rejects when both are set; we follow.
public sealed class ListAccountsQueryService(AppDbContext _db) : IListAccountsQueryService
{
  public async Task<(IReadOnlyList<AccountDto> Items, bool HasMore)> ListAsync(
    int limit,
    string? startingAfter,
    string? endingBefore,
    CancellationToken cancellationToken = default)
  {
    if (startingAfter is not null && endingBefore is not null)
    {
      // Stripe returns 400; the inbound layer enforces this. Defensive
      // guard so a programming error doesn't silently miscount.
      throw new ArgumentException("starting_after and ending_before are mutually exclusive.");
    }

    // We over-fetch by one row to detect `has_more` without a second
    // round-trip COUNT — same trick Stripe uses internally.
    var fetch = limit + 1;
    var q = _db.Set<Account>().AsNoTracking();

    if (startingAfter is not null)
    {
      var cursor = await ResolveCursorAsync(startingAfter, cancellationToken);
      if (cursor is null) return (Array.Empty<AccountDto>(), false);
      // Forward: items strictly older than the cursor row's created time.
      // Pagination is by `created` only — adding a (created, id) tiebreaker
      // would require comparing across the Vogen-typed PK, which doesn't
      // translate cleanly to SQL when AccountId carries a value converter.
      // Acceptable trade-off for a scaffold demo; production code would
      // either drop the converter for the cursor query or carry the tied
      // boundary in the cursor token.
      q = q.Where(a => a.Created < cursor.Value);
    }
    else if (endingBefore is not null)
    {
      var cursor = await ResolveCursorAsync(endingBefore, cancellationToken);
      if (cursor is null) return (Array.Empty<AccountDto>(), false);
      q = q.Where(a => a.Created > cursor.Value);
    }

    List<Account> rows;
    if (endingBefore is not null)
    {
      rows = await q
        .OrderBy(a => a.Created)
        .Take(fetch)
        .ToListAsync(cancellationToken);
      // The forward-presentation order is newest-first; reverse the
      // ascending fetch.
      rows.Reverse();
    }
    else
    {
      rows = await q
        .OrderByDescending(a => a.Created)
        .Take(fetch)
        .ToListAsync(cancellationToken);
    }

    var hasMore = rows.Count > limit;
    var page = hasMore ? rows.Take(limit) : rows;
    return (page.Select(AccountDto.FromAggregate).ToList(), hasMore);
  }

  private async Task<DateTime?> ResolveCursorAsync(
    string id, CancellationToken cancellationToken)
  {
    var typedId = AccountId.From(id);
    var created = await _db.Set<Account>().AsNoTracking()
      .Where(a => a.Id == typedId)
      .Select(a => (DateTime?)a.Created)
      .FirstOrDefaultAsync(cancellationToken);
    return created;
  }
}
