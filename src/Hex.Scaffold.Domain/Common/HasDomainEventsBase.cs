using System.ComponentModel.DataAnnotations.Schema;

namespace Hex.Scaffold.Domain.Common;

public abstract class HasDomainEventsBase
{
  private readonly List<DomainEventBase> _domainEvents = [];

  [NotMapped]
  public IReadOnlyList<DomainEventBase> DomainEvents => _domainEvents.AsReadOnly();

  protected void RegisterDomainEvent(DomainEventBase domainEvent) =>
    _domainEvents.Add(domainEvent);

  public void ClearDomainEvents() => _domainEvents.Clear();
}
