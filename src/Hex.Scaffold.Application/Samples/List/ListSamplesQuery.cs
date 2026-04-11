namespace Hex.Scaffold.Application.Samples.List;

public record ListSamplesQuery(int? Page = 1, int? PerPage = Constants.DefaultPageSize)
  : IQuery<Result<PagedResult<SampleDto>>>;
