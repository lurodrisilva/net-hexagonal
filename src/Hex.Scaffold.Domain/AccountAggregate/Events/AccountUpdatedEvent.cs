namespace Hex.Scaffold.Domain.AccountAggregate.Events;

public sealed class AccountUpdatedEvent(Account account) : DomainEventBase
{
  public Account Account { get; init; } = account;
}
