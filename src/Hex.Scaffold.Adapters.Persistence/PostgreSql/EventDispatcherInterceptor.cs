using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hex.Scaffold.Adapters.Persistence.PostgreSql;

public sealed class EventDispatcherInterceptor(IDomainEventDispatcher _dispatcher)
  : SaveChangesInterceptor
{
  public override async ValueTask<int> SavedChangesAsync(
    SaveChangesCompletedEventData eventData,
    int result,
    CancellationToken cancellationToken = default)
  {
    if (eventData.Context is not null)
    {
      var entitiesWithEvents = eventData.Context.ChangeTracker
        .Entries<HasDomainEventsBase>()
        .Select(e => e.Entity)
        .Where(e => e.DomainEvents.Any())
        .ToList();

      await _dispatcher.DispatchAndClearEvents(entitiesWithEvents);
    }

    return await base.SavedChangesAsync(eventData, result, cancellationToken);
  }
}
