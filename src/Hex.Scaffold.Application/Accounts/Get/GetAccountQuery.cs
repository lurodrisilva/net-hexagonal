using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Application.Accounts.Get;

public sealed record GetAccountQuery(AccountId Id) : IQuery<Result<AccountDto>>;
