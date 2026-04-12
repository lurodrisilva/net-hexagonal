using Hex.Scaffold.Domain.Common;

namespace Hex.Scaffold.Adapters.Persistence.Common;

public sealed class MediatorDomainEventDispatcher(IMediator _mediator) : IDomainEventDispatcher
{
  public async Task DispatchAndClearEvents(IEnumerable<HasDomainEventsBase> entitiesWithEvents)
  {
    foreach (var entity in entitiesWithEvents)
    {
      var events = entity.DomainEvents.ToArray();
      entity.ClearDomainEvents();

      foreach (var domainEvent in events)
      {
        await _mediator.Publish(domainEvent);
      }
    }
  }
}
