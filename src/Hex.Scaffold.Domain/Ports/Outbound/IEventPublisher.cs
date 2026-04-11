namespace Hex.Scaffold.Domain.Ports.Outbound;

public interface IEventPublisher
{
  ValueTask PublishAsync<TEvent>(string topic, TEvent @event, CancellationToken cancellationToken = default)
    where TEvent : class;
}
