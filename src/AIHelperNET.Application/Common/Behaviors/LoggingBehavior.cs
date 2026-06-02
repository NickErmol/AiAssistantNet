using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AIHelperNET.Application.Common.Behaviors;

/// <summary>Pipeline behavior that logs request start, completion time, and unhandled exceptions.</summary>
/// <typeparam name="TMessage">The mediator message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed partial class LoggingBehavior<TMessage, TResponse>(ILogger<TMessage> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    /// <inheritdoc/>
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TMessage).Name;
        Log.Handling(logger, name);
        var sw = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(message, cancellationToken);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var elapsed = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
                Log.Handled(logger, name, elapsed);
            }
            return response;
        }
        catch (Exception ex)
        {
            Log.UnhandledException(logger, name, ex);
            throw;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Request}")]
        internal static partial void Handling(ILogger logger, string request);

        [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Request} in {Elapsed}ms")]
        internal static partial void Handled(ILogger logger, string request, double elapsed);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in {Request}")]
        internal static partial void UnhandledException(ILogger logger, string request, Exception ex);
    }
}
