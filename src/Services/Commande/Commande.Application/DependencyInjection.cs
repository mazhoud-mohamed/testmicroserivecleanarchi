using Commande.Application.Abstractions;
using Commande.Application.Decorateurs;
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

        // Chaque IHandler<,> résolu sera enveloppé par le décorateur de logs.
        services.Decorate(typeof(IHandler<,>), typeof(HandlerAvecLogs<,>));
        return services;
    }
}
