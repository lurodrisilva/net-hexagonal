using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.AccountAggregate.Specifications;
using Hex.Scaffold.Domain.Ports.Outbound;

namespace Hex.Scaffold.Application.Accounts.Get;

public sealed class GetAccountHandler(
  IReadRepository<Account> _repository,
  ICacheService _cache,
  ILogger<GetAccountHandler> _logger)
  : IQueryHandler<GetAccountQuery, Result<AccountDto>>
{
  // Stripe's docs don't promise a strict TTL; 5 min mirrors the previous
  // GetSampleHandler value and lines up with the cache-invalidation cadence
  // in AccountEventPublishHandler.
  private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

  public async ValueTask<Result<AccountDto>> Handle(
    GetAccountQuery request, CancellationToken cancellationToken)
  {
    var cacheKey = $"account:{request.Id.Value}";

    var cached = await _cache.GetAsync<AccountDto>(cacheKey, cancellationToken);
    if (cached is not null)
    {
      _logger.LogDebug("Cache hit for Account {AccountId}", request.Id);
      return cached;
    }

    var account = await _repository.FirstOrDefaultAsync(
      new AccountByIdSpec(request.Id), cancellationToken);
    if (account is null) return Result<AccountDto>.NotFound();

    var dto = AccountDto.FromAggregate(account);
    await _cache.SetAsync(cacheKey, dto, CacheTtl, cancellationToken);
    return dto;
  }
}
