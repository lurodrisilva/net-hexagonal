using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.AccountAggregate.Events;

namespace Hex.Scaffold.Application.Accounts.Create;

public sealed class CreateAccountHandler(
  IRepository<Account> _repository,
  IMediator _mediator,
  ILogger<CreateAccountHandler> _logger)
  : ICommandHandler<CreateAccountCommand, Result<AccountDto>>
{
  public async ValueTask<Result<AccountDto>> Handle(
    CreateAccountCommand command,
    CancellationToken cancellationToken)
  {
    _logger.LogInformation("Creating Account (livemode={Livemode})", command.Livemode);

    // Account.Create generates the acct_… ID before the entity hits
    // ChangeTracker — see the class doc for why that ordering matters.
    var account = Account.Create(
      livemode: command.Livemode,
      displayName: command.DisplayName,
      contactEmail: command.ContactEmail,
      contactPhone: command.ContactPhone,
      appliedConfigurations: command.AppliedConfigurations,
      configurationJson: command.ConfigurationJson,
      identityJson: command.IdentityJson,
      defaultsJson: command.DefaultsJson,
      metadataJson: command.MetadataJson);

    var created = await _repository.AddAsync(account, cancellationToken);

    await _mediator.Publish(new AccountCreatedEvent(created), cancellationToken);

    return AccountDto.FromAggregate(created);
  }
}
