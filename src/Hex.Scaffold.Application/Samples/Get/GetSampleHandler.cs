using Hex.Scaffold.Domain.Ports.Outbound;
using Hex.Scaffold.Domain.SampleAggregate;
using Hex.Scaffold.Domain.SampleAggregate.Specifications;

namespace Hex.Scaffold.Application.Samples.Get;

public sealed class GetSampleHandler(
  IReadRepository<Sample> _repository,
  ICacheService _cache,
  ILogger<GetSampleHandler> _logger)
  : IQueryHandler<GetSampleQuery, Result<SampleDto>>
{
  private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

  public async ValueTask<Result<SampleDto>> Handle(
    GetSampleQuery request,
    CancellationToken cancellationToken)
  {
    var cacheKey = $"sample:{request.SampleId.Value}";

    var cached = await _cache.GetAsync<SampleDto>(cacheKey, cancellationToken);
    if (cached is not null)
    {
      _logger.LogDebug("Cache hit for Sample {SampleId}", request.SampleId);
      return cached;
    }

    var spec = new SampleByIdSpec(request.SampleId);
    var sample = await _repository.FirstOrDefaultAsync(spec, cancellationToken);
    if (sample is null) return Result<SampleDto>.NotFound();

    var dto = new SampleDto(sample.Id, sample.Name, sample.Status, sample.Description);
    await _cache.SetAsync(cacheKey, dto, CacheTtl, cancellationToken);

    return dto;
  }
}
