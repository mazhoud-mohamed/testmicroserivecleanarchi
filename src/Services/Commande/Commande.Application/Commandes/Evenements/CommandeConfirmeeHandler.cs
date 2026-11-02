using Commande.Application.Abstractions;
using Commande.Domain.Commandes.Evenements;
using Contrats.Commandes;
using Microsoft.Extensions.Logging;

namespace Commande.Application.Commandes.Evenements;

// ÉTAPE 4 du trajet : le répartiteur (DomainEventDispatcher) appelle ce handler quand un
// Domain Event « CommandeConfirmee » a été levé par la commande.
//
// Son rôle : TRADUIRE l'événement interne (Domain Event) en message public (Integration Event)
// et le confier à l'IEventBus pour qu'il parte vers les autres services.
public class CommandeConfirmeeHandler(IEventBus eventBus, ILogger<CommandeConfirmeeHandler> logger)
    : IDomainEventHandler<CommandeConfirmee>
{
    public async Task Handle(CommandeConfirmee evenement, CancellationToken ct)
    {
        logger.LogInformation("Événement CommandeConfirmee reçu : commande {CommandeId}, client {ClientId}, montant {MontantTotal}",
            evenement.CommandeId, evenement.ClientId, evenement.MontantTotal);

        // On copie seulement ce dont les autres services ont besoin, dans le format du contrat.
        // evenement.Id est réutilisé comme EventId : un même fait garde le même identifiant de bout en bout.
        var integration = new CommandeConfirmeeIntegrationEvent(
            evenement.Id,
            evenement.SurvenueLe,
            evenement.CommandeId,
            evenement.ClientId,
            evenement.Lignes.Select(l => new LigneCommandeConfirmee(l.ProduitId, l.Quantite)).ToList());

        // ÉTAPE 5 : envoi vers RabbitMQ (voir RabbitMqEventBus).
        // Attention : ce handler s'exécute AVANT l'écriture en base. Si l'écriture échoue ensuite,
        // le message est déjà parti : c'est le problème que le pattern Outbox corrigera.
        await eventBus.PublierAsync(integration, CommandeConfirmeeIntegrationEvent.CleRoutage, ct);
    }
}
