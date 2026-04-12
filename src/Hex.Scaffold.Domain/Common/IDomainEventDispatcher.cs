namespace Hex.Scaffold.Domain.Common;

public interface IDomainEventDispatcher
{
  Task DispatchAndClearEvents(IEnumerable<HasDomainEventsBase> entitiesWithEvents);
}
