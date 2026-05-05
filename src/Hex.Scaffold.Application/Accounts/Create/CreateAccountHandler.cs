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

    // Idempotency guard for at-least-once Kafka delivery — when the command
    // carries an externally-supplied id (Kafka inbound load-test path), a
    // replayed AccountCreatedEvent must not produce a duplicate aggregate.
    if (command.Id is { } externalId)
    {
      var existing = await _repository.GetByIdAsync<AccountId>(externalId, cancellationToken);
      if (existing is not null)
      {
        _logger.LogDebug("AccountCreatedEvent replay for {AccountId} — skipping", externalId);
        return AccountDto.FromAggregate(existing);
      }

      var withId = Account.CreateWithExternalId(
        id: externalId,
        livemode: command.Livemode,
        displayName: command.DisplayName,
        contactEmail: command.ContactEmail,
        contactPhone: command.ContactPhone,
        appliedConfigurations: command.AppliedConfigurations,
        configurationJson: command.ConfigurationJson,
        identityJson: command.IdentityJson,
        defaultsJson: command.DefaultsJson,
        metadataJson: command.MetadataJson);

      var addedWithId = await _repository.AddAsync(withId, cancellationToken);
      await _mediator.Publish(new AccountCreatedEvent(addedWithId), cancellationToken);
      return AccountDto.FromAggregate(addedWithId);
    }

    // Original REST path — Account.Create generates the acct_… ID before the
    // entity hits ChangeTracker (see Account class doc).
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
