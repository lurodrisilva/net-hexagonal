namespace Hex.Scaffold.Application.Samples.List;

public sealed class ListSamplesHandler(IListSamplesQueryService _queryService)
  : IQueryHandler<ListSamplesQuery, Result<PagedResult<SampleDto>>>
{
  public async ValueTask<Result<PagedResult<SampleDto>>> Handle(
    ListSamplesQuery request,
    CancellationToken cancellationToken)
  {
    var result = await _queryService.ListAsync(
      request.Page ?? 1,
      request.PerPage ?? Constants.DefaultPageSize,
      cancellationToken);

    return Result.Success(result);
  }
}
