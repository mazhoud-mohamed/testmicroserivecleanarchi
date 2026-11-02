using Commande.Application.Abstractions;
using Commande.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Commande.Application.Evenements;

// ÉTAPE 3 du trajet : le « standard téléphonique » des Domain Events.
// Pour chaque événement reçu, il retrouve TOUS les handlers enregistrés pour son type et les appelle.
// Un événement peut avoir zéro, un ou plusieurs handlers.
public class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> evenements, CancellationToken ct)
    {
        foreach (var evenement in evenements)
        {
            // Le type exact n'est connu qu'à l'exécution (CommandeConfirmee...) : on passe par la réflexion.
            // Exemple : pour CommandeConfirmee, on cherche IDomainEventHandler<CommandeConfirmee>.
            var typeHandler = typeof(IDomainEventHandler<>).MakeGenericType(evenement.GetType());
            var methode = typeHandler.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;

            foreach (var handler in services.GetServices(typeHandler))
                await (Task)methode.Invoke(handler, [evenement, ct])!;
        }
    }
}
