using Hex.Scaffold.Domain.Ports.Outbound;
using Hex.Scaffold.Domain.SampleAggregate.Events;

namespace Hex.Scaffold.Domain.SampleAggregate.Handlers;

public sealed class SampleEventPublishHandler(
  IEventPublisher _eventPublisher,
  ICacheService _cacheService,
  ILogger<SampleEventPublishHandler> _logger)
  : INotificationHandler<SampleCreatedEvent>,
    INotificationHandler<SampleUpdatedEvent>,
    INotificationHandler<SampleDeletedEvent>
{
  private const string KafkaTopic = "sample-events";

  public async ValueTask Handle(SampleCreatedEvent notification, CancellationToken cancellationToken)
  {
    _logger.LogInformation("Publishing SampleCreatedEvent for Sample {SampleId}", notification.Sample.Id);
    await _eventPublisher.PublishAsync(KafkaTopic, notification, cancellationToken);
    await _cacheService.RemoveAsync("samples:list", cancellationToken);
  }

  public async ValueTask Handle(SampleUpdatedEvent notification, CancellationToken cancellationToken)
  {
    _logger.LogInformation("Publishing SampleUpdatedEvent for Sample {SampleId}", notification.Sample.Id);
    await _eventPublisher.PublishAsync(KafkaTopic, notification, cancellationToken);
    await _cacheService.RemoveAsync($"sample:{notification.Sample.Id.Value}", cancellationToken);
    await _cacheService.RemoveAsync("samples:list", cancellationToken);
  }

  public async ValueTask Handle(SampleDeletedEvent notification, CancellationToken cancellationToken)
  {
    _logger.LogInformation("Publishing SampleDeletedEvent for Sample {SampleId}", notification.SampleId);
    await _eventPublisher.PublishAsync(KafkaTopic, notification, cancellationToken);
    await _cacheService.RemoveAsync($"sample:{notification.SampleId.Value}", cancellationToken);
    await _cacheService.RemoveAsync("samples:list", cancellationToken);
  }
}
