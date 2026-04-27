using Hex.Scaffold.Domain.AccountAggregate.Events;
using Hex.Scaffold.Domain.Ports.Outbound;

namespace Hex.Scaffold.Domain.AccountAggregate.Handlers;

// Bridges domain notifications to the outbound IEventPublisher (Kafka in
// production, NoOp fallback when outbound=rest — see PR #21). The cache
// invalidation lines mirror the same pattern PR #21's classic-AI Live
// Metrics work covers: per-account key + the list bucket.
public sealed class AccountEventPublishHandler(
  IEventPublisher _eventPublisher,
  ICacheService _cacheService,
  ILogger<AccountEventPublishHandler> _logger)
  : INotificationHandler<AccountCreatedEvent>,
    INotificationHandler<AccountUpdatedEvent>
{
  // Topic mirrors the Stripe v2 webhook event-name space; the consumer side
  // (when inbound=kafka) embeds the event type in the message body.
  private const string KafkaTopic = "v2.core.accounts";

  public async ValueTask Handle(AccountCreatedEvent notification, CancellationToken cancellationToken)
  {
    _logger.LogInformation(
      "Publishing v2.core.account.created for {AccountId}", notification.Account.Id);
    await _eventPublisher.PublishAsync(KafkaTopic, notification, cancellationToken);
    await _cacheService.RemoveAsync("accounts:list", cancellationToken);
  }

  public async ValueTask Handle(AccountUpdatedEvent notification, CancellationToken cancellationToken)
  {
    _logger.LogInformation(
      "Publishing v2.core.account.updated for {AccountId}", notification.Account.Id);
    await _eventPublisher.PublishAsync(KafkaTopic, notification, cancellationToken);
    await _cacheService.RemoveAsync($"account:{notification.Account.Id.Value}", cancellationToken);
    await _cacheService.RemoveAsync("accounts:list", cancellationToken);
  }
}
