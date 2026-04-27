namespace Hex.Scaffold.Application.Accounts.List;

// Cursor-based pagination port. The persistence-side implementation reads
// over `created DESC, id DESC` so cursors stay stable under inserts.
// Returns (items, hasMore) — neither total count nor page numbers, in line
// with Stripe's list envelope.
public interface IListAccountsQueryService
{
  Task<(IReadOnlyList<AccountDto> Items, bool HasMore)> ListAsync(
    int limit,
    string? startingAfter,
    string? endingBefore,
    CancellationToken cancellationToken = default);
}
