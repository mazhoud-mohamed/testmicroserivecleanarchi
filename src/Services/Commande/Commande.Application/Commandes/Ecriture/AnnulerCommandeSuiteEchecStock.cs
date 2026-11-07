using Commande.Application.Abstractions;
using Commande.Domain.Commandes;
using Microsoft.Extensions.Logging;

namespace Commande.Application.Commandes.Ecriture;

// COMPENSATION de la saga : Stock a annoncé qu'il ne peut pas réserver, on défait la confirmation.
// MessageId : l'identifiant du message reçu (EventId). Il sert à reconnaître un message déjà traité.
public record AnnulerCommandeSuiteEchecStock(Guid MessageId, Guid CommandeId, string Motif);

// Issue du traitement. Aucune n'est une erreur technique : ce sont toutes des issues métier normales,
// pour lesquelles le message doit être acquitté (le rejouer ne changerait rien).
public enum ResultatAnnulation
{
    Annulee,
    DejaTraite,
    DejaAnnulee,
    CommandeIntrouvable,
    NonAnnulable
}

public class AnnulerCommandeSuiteEchecStockHandler(
    ICommandeRepository repository,
    IMessagesTraites messagesTraites,
    ILogger<AnnulerCommandeSuiteEchecStockHandler> logger)
    : IHandler<AnnulerCommandeSuiteEchecStock, ResultatAnnulation>
{
    public async Task<ResultatAnnulation> Handle(AnnulerCommandeSuiteEchecStock demande, CancellationToken ct)
    {
        // INBOX : RabbitMQ garantit « au moins une fois », donc ce message peut arriver deux fois.
        if (await messagesTraites.ADejaEteTraiteAsync(demande.MessageId, ct))
        {
            logger.LogWarning("Message {MessageId} déjà traité (commande {CommandeId}), ignoré",
                demande.MessageId, demande.CommandeId);
            return ResultatAnnulation.DejaTraite;
        }

        // Dans tous les cas ci-dessous le message est marqué traité, dans le même SaveChanges que le reste.
        // Le rejouer donnerait le même résultat : on évite ainsi toute boucle de réessais.
        messagesTraites.MarquerCommeTraite(demande.MessageId);

        var commande = await repository.ObtenirParIdAsync(demande.CommandeId, ct);
        if (commande is null)
        {
            await repository.SauvegarderAsync(ct);
            logger.LogWarning("Échec de stock reçu pour une commande introuvable {CommandeId} : {Motif}",
                demande.CommandeId, demande.Motif);
            return ResultatAnnulation.CommandeIntrouvable;
        }

        // Idempotent : annulée entre-temps (par le client, par exemple), il n'y a plus rien à faire.
        if (commande.Statut == StatutCommande.Annulee)
        {
            await repository.SauvegarderAsync(ct);
            logger.LogInformation("Commande {CommandeId} déjà annulée, échec de stock ignoré", demande.CommandeId);
            return ResultatAnnulation.DejaAnnulee;
        }

        // Déjà expédiée : la compensation automatique est impossible. On n'annule pas et on signale fort :
        // c'est un cas qui demande une action humaine.
        if (commande.Statut == StatutCommande.Expediee)
        {
            await repository.SauvegarderAsync(ct);
            logger.LogError(
                "ALERTE : le stock n'a pas pu être réservé pour la commande {CommandeId}, mais elle est déjà expédiée. Motif : {Motif}",
                demande.CommandeId, demande.Motif);
            return ResultatAnnulation.NonAnnulable;
        }

        commande.Annuler(demande.Motif);
        await repository.SauvegarderAsync(ct);

        logger.LogWarning("Commande {CommandeId} annulée suite à un échec de réservation du stock : {Motif}",
            demande.CommandeId, demande.Motif);
        return ResultatAnnulation.Annulee;
    }
}
