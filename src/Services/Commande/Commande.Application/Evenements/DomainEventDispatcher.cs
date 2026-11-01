using Commande.Application.Abstractions;
using Commande.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Commande.Application.Evenements;

// Pour chaque événement, retrouve tous les IDomainEventHandler<TonEvenement> enregistrés et les appelle.
public class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> evenements, CancellationToken ct)
    {
        foreach (var evenement in evenements)
        {
            // Le type exact n'est connu qu'à l'exécution (CommandeConfirmee...) : on passe par la réflexion.
            var typeHandler = typeof(IDomainEventHandler<>).MakeGenericType(evenement.GetType());
            var methode = typeHandler.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;

            foreach (var handler in services.GetServices(typeHandler))
                await (Task)methode.Invoke(handler, [evenement, ct])!;
        }
    }
}
