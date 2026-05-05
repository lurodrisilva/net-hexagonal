namespace Hex.Scaffold.Domain.AccountAggregate.Events;

public sealed class AccountDeletedEvent(AccountId id, DateTimeOffset deletedAtUtc) : DomainEventBase
{
  public AccountId Id { get; init; } = id;
  public DateTimeOffset DeletedAtUtc { get; init; } = deletedAtUtc;
}
