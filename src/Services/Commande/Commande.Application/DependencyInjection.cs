using Commande.Application.Abstractions;
using Commande.Application.Decorateurs;
using Commande.Application.Evenements;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Commande.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Enregistre automatiquement tous les IHandler<,> de cet assembly.
        services.Scan(scan => scan
            .FromAssemblyOf<IHandler<object, object>>()
            .AddClasses(c => c.AssignableTo(typeof(IHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        // Enregistre automatiquement tous les IDomainEventHandler<> et le répartiteur d'événements.
        services.Scan(scan => scan
            .FromAssemblyOf<IHandler<object, object>>()
            .AddClasses(c => c.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // Enregistre tous les validateurs FluentValidation de cet assembly.
        services.AddValidatorsFromAssemblyContaining<IHandler<object, object>>();

        // Le dernier Decorate est le plus extérieur : logs -> validation -> handler.
        services.Decorate(typeof(IHandler<,>), typeof(HandlerAvecValidation<,>));
        services.Decorate(typeof(IHandler<,>), typeof(HandlerAvecLogs<,>));
        return services;
    }
}
