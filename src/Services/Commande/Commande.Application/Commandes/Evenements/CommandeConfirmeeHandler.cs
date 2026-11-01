using Commande.Application.Abstractions;
using Commande.Domain.Commandes.Evenements;
using Microsoft.Extensions.Logging;

namespace Commande.Application.Commandes.Evenements;

// Exemple de réaction à un événement de domaine. Plus tard, c'est ici qu'on préparera l'événement d'intégration.
public class CommandeConfirmeeHandler(ILogger<CommandeConfirmeeHandler> logger)
    : IDomainEventHandler<CommandeConfirmee>
{
    public Task Handle(CommandeConfirmee evenement, CancellationToken ct)
    {
        logger.LogInformation("Événement CommandeConfirmee reçu : commande {CommandeId}, client {ClientId}, montant {MontantTotal}",
            evenement.CommandeId, evenement.ClientId, evenement.MontantTotal);
        return Task.CompletedTask;
    }
}
