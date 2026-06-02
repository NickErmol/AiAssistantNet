using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Common.Behaviors;
using AIHelperNET.Application.Sessions;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.Application;

/// <summary>Registers all Application layer services into the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>Adds CQRS handlers, pipeline behaviors, validators, and application services.</summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(static options =>
            options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddSingleton<SessionMapper>();
        services.AddSingleton<PromptBuilderService>();

        return services;
    }
}
