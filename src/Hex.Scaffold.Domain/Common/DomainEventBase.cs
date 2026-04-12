using Mediator;

namespace Hex.Scaffold.Domain.Common;

public abstract class DomainEventBase : INotification
{
  public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
