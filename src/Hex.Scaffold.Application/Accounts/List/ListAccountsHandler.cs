namespace Hex.Scaffold.Application.Accounts.List;

public sealed class ListAccountsHandler(IListAccountsQueryService _service)
  : IQueryHandler<ListAccountsQuery, Result<AccountListResult>>
{
  public const int DefaultLimit = 10;
  public const int MaxLimit = 100;

  public async ValueTask<Result<AccountListResult>> Handle(
    ListAccountsQuery query, CancellationToken cancellationToken)
  {
    var limit = Math.Clamp(query.Limit <= 0 ? DefaultLimit : query.Limit, 1, MaxLimit);
    var (items, hasMore) = await _service.ListAsync(
      limit, query.StartingAfter, query.EndingBefore, cancellationToken);
    return AccountListResult.Wrap(items, hasMore);
  }
}
