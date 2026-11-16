using Commande.Application.Abstractions;
using Commande.Domain.Commandes;
using Microsoft.Extensions.Logging;

namespace Commande.Application.Commandes.Ecriture;

// CHEMIN ALLER de la saga, issue favorable : Stock a annoncé qu'il a réservé les articles.
// La commande devient expédiable. MessageId : l'identifiant du message reçu, pour reconnaître un doublon.
public record MarquerStockReserve(Guid MessageId, Guid CommandeId);

// Aucune de ces issues n'est une erreur technique : le message doit être acquitté dans tous les cas
// (le rejouer ne changerait rien).
public enum ResultatReservationStock
{
    StockReserve,
    DejaTraite,
    DejaReserve,
    CommandeAnnulee,
    CommandeIntrouvable,
    StatutInattendu
}

public class MarquerStockReserveHandler(
    ICommandeRepository repository,
    IMessagesTraites messagesTraites,
    ILogger<MarquerStockReserveHandler> logger)
    : IHandler<MarquerStockReserve, ResultatReservationStock>
{
    public async Task<ResultatReservationStock> Handle(MarquerStockReserve demande, CancellationToken ct)
    {
        // INBOX : RabbitMQ garantit « au moins une fois », donc ce message peut arriver deux fois.
        if (await messagesTraites.ADejaEteTraiteAsync(demande.MessageId, ct))
        {
            logger.LogWarning("Message {MessageId} déjà traité (commande {CommandeId}), ignoré",
                demande.MessageId, demande.CommandeId);
            return ResultatReservationStock.DejaTraite;
        }

        // Dans tous les cas ci-dessous le message est marqué traité, dans le même SaveChanges que le reste.
        messagesTraites.MarquerCommeTraite(demande.MessageId);

        var commande = await repository.ObtenirParIdAsync(demande.CommandeId, ct);
        if (commande is null)
        {
            await repository.SauvegarderAsync(ct);
            logger.LogWarning("Réservation de stock reçue pour une commande introuvable {CommandeId}", demande.CommandeId);
            return ResultatReservationStock.CommandeIntrouvable;
        }

        switch (commande.Statut)
        {
            case StatutCommande.Confirmee:
                commande.MarquerStockReserve();
                await repository.SauvegarderAsync(ct);
                logger.LogInformation("Stock réservé pour la commande {CommandeId} : elle peut être expédiée", demande.CommandeId);
                return ResultatReservationStock.StockReserve;

            case StatutCommande.StockReserve:
                await repository.SauvegarderAsync(ct);
                logger.LogInformation("Commande {CommandeId} déjà marquée stock réservé, message ignoré", demande.CommandeId);
                return ResultatReservationStock.DejaReserve;

            case StatutCommande.Annulee:
                // Le client a annulé pendant que Stock réservait : le stock reste réservé pour une commande annulée.
                // La libération du stock n'existe pas encore : on ne peut que le signaler, c'est une action humaine.
                await repository.SauvegarderAsync(ct);
                logger.LogError(
                    "ALERTE : Stock a réservé des articles pour la commande {CommandeId}, mais elle est déjà annulée. Le stock doit être libéré.",
                    demande.CommandeId);
                return ResultatReservationStock.CommandeAnnulee;

            default:
                await repository.SauvegarderAsync(ct);
                logger.LogWarning("Réservation de stock reçue pour la commande {CommandeId} au statut {Statut}, ignorée",
                    demande.CommandeId, commande.Statut);
                return ResultatReservationStock.StatutInattendu;
        }
    }
}
