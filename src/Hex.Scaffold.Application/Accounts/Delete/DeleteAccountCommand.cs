using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Application.Accounts.Delete;

public sealed record DeleteAccountCommand(AccountId Id) : ICommand<Result>;
