using FluentResults;
using FluentValidation;
using Mediator;

namespace AIHelperNET.Application.Common.Behaviors;

/// <summary>Pipeline behavior that validates requests using FluentValidation before dispatching.</summary>
/// <typeparam name="TMessage">The mediator message type.</typeparam>
/// <typeparam name="TResponse">The FluentResults response type.</typeparam>
public sealed class ValidationBehavior<TMessage, TResponse>(
    IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
    where TResponse : IResultBase, new()
{
    /// <inheritdoc/>
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any()) return await next(message, cancellationToken);

        var context = new ValidationContext<TMessage>(message);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0) return await next(message, cancellationToken);

        var result = new TResponse();
        foreach (var f in failures)
            result.Reasons.Add(new Error(f.ErrorMessage));
        return result;
    }
}
