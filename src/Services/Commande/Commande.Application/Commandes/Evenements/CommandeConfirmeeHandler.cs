using Commande.Application.Abstractions;
using Commande.Domain.Commandes.Evenements;
using Contrats.Commandes;
using Microsoft.Extensions.Logging;

namespace Commande.Application.Commandes.Evenements;

// ÉTAPE 4 du trajet : le répartiteur (DomainEventDispatcher) appelle ce handler quand un
// Domain Event « CommandeConfirmee » a été levé par la commande.
//
// Son rôle : TRADUIRE l'événement interne (Domain Event) en message public (Integration Event)
// et le déposer dans l'Outbox pour qu'il parte plus tard vers les autres services.
public class CommandeConfirmeeHandler(IOutbox outbox, ILogger<CommandeConfirmeeHandler> logger)
    : IDomainEventHandler<CommandeConfirmee>
{
    public Task Handle(CommandeConfirmee evenement, CancellationToken ct)
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

        // ÉTAPE 5 : dépôt dans l'Outbox. Rien n'est envoyé ici : la ligne est ajoutée au DbContext et sera
        // écrite par le SaveChanges en cours, dans la même transaction que le changement de statut.
        // Soit les deux sont enregistrés, soit aucun : plus de message pour une commande non confirmée.
        outbox.Ajouter(integration, CommandeConfirmeeIntegrationEvent.CleRoutage);
        return Task.CompletedTask;
    }
}
