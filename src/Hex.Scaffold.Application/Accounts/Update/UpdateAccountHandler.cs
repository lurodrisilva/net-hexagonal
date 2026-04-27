using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.AccountAggregate.Specifications;

namespace Hex.Scaffold.Application.Accounts.Update;

public sealed class UpdateAccountHandler(
  IRepository<Account> _repository,
  ILogger<UpdateAccountHandler> _logger)
  : ICommandHandler<UpdateAccountCommand, Result<AccountDto>>
{
  public async ValueTask<Result<AccountDto>> Handle(
    UpdateAccountCommand command,
    CancellationToken cancellationToken)
  {
    _logger.LogInformation("Updating Account {AccountId}", command.Id);

    var account = await _repository.FirstOrDefaultAsync(
      new AccountByIdSpec(command.Id), cancellationToken);
    if (account is null) return Result<AccountDto>.NotFound();

    account.ApplyUpdate(
      displayName:           command.DisplayName,
      contactEmail:          command.ContactEmail,
      contactPhone:          command.ContactPhone,
      appliedConfigurations: command.AppliedConfigurations,
      configurationJson:     command.ConfigurationJson,
      identityJson:          command.IdentityJson,
      defaultsJson:          command.DefaultsJson,
      metadataJson:          command.MetadataJson);

    await _repository.UpdateAsync(account, cancellationToken);

    return AccountDto.FromAggregate(account);
  }
}
