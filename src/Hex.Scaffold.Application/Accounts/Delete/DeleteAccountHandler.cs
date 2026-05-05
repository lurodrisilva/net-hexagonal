using Hex.Scaffold.Domain.AccountAggregate;

namespace Hex.Scaffold.Application.Accounts.Delete;

public sealed class DeleteAccountHandler(
  IRepository<Account> _repository,
  ILogger<DeleteAccountHandler> _logger)
  : ICommandHandler<DeleteAccountCommand, Result>
{
  public async ValueTask<Result> Handle(
    DeleteAccountCommand command,
    CancellationToken cancellationToken)
  {
    _logger.LogInformation("Deleting Account {AccountId}", command.Id);

    var account = await _repository.GetByIdAsync<AccountId>(command.Id, cancellationToken);
    if (account is null)
    {
      _logger.LogInformation("Account {AccountId} not found — treating as successful no-op (idempotent delete)", command.Id);
      return Result.Success();
    }

    account.MarkDeleted();
    await _repository.DeleteAsync(account, cancellationToken);

    return Result.Success();
  }
}
