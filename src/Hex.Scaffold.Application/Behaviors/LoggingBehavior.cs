using System.Diagnostics;

namespace Hex.Scaffold.Application.Behaviors;

public sealed class LoggingBehavior<TMessage, TResponse>(
  ILogger<LoggingBehavior<TMessage, TResponse>> _logger)
  : IPipelineBehavior<TMessage, TResponse>
  where TMessage : notnull, IMessage
{
  public async ValueTask<TResponse> Handle(
    TMessage message,
    MessageHandlerDelegate<TMessage, TResponse> next,
    CancellationToken cancellationToken)
  {
    var messageName = typeof(TMessage).Name;
    _logger.LogInformation("Handling {MessageName}", messageName);

    var sw = Stopwatch.StartNew();
    var response = await next(message, cancellationToken);
    sw.Stop();

    _logger.LogInformation("Handled {MessageName} in {ElapsedMs}ms", messageName, sw.ElapsedMilliseconds);
    return response;
  }
}
