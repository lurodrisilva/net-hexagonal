namespace Hex.Scaffold.Application.Samples.List;

public interface IListSamplesQueryService
{
  Task<PagedResult<SampleDto>> ListAsync(int page, int perPage, CancellationToken cancellationToken = default);
}
