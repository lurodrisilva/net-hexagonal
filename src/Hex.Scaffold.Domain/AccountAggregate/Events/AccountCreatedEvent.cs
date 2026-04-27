namespace Hex.Scaffold.Domain.AccountAggregate.Events;

public sealed class AccountCreatedEvent(Account account) : DomainEventBase
{
  public Account Account { get; init; } = account;
}
