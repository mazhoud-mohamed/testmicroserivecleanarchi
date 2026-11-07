using Contrats.Stock;
using Microsoft.Extensions.Logging;
using Stock.Application.Abstractions;

namespace Stock.Application.Articles;

// Types propres à Stock : le cas d'usage ne dépend ni de RabbitMQ ni du format du message de Commande.
// C'est le consommateur (Infrastructure) qui traduit le message en cette demande.
public record LigneAReserver(Guid ProduitId, int Quantite);

// MessageId : l'identifiant du message reçu (EventId). Il sert à reconnaître un message déjà traité.
public record ReserverStockPourCommande(Guid MessageId, Guid CommandeId, IReadOnlyList<LigneAReserver> Lignes);

// Issue du traitement. Un refus (produit inconnu, stock insuffisant) n'est PAS une erreur technique :
// c'est une issue métier normale, qui déclenche la compensation de la saga.
public enum ResultatReservation
{
    Reservee,
    Refusee,
    DejaTraite
}

// Réserve le stock de TOUTES les lignes d'une commande, ou d'aucune.
// En cas de refus, il n'y a ni exception ni message perdu : Stock publie « ReservationEchouee » pour que Commande annule.
public class ReserverStockPourCommandeHandler(
    IStockRepository repository,
    IMessagesTraites messagesTraites,
    IOutbox outbox,
    ILogger<ReserverStockPourCommandeHandler> logger)
{
    public async Task<ResultatReservation> Handle(ReserverStockPourCommande demande, CancellationToken ct)
    {
        // INBOX : RabbitMQ garantit « au moins une fois », donc ce message peut arriver deux fois.
        // S'il a déjà été traité (réservé OU refusé), on s'arrête là : ni nouvelle réservation, ni second échec publié.
        if (await messagesTraites.ADejaEteTraiteAsync(demande.MessageId, ct))
        {
            logger.LogWarning("Message {MessageId} déjà traité (commande {CommandeId}), ignoré",
                demande.MessageId, demande.CommandeId);
            return ResultatReservation.DejaTraite;
        }

        // Si la commande contient deux fois le même produit, on additionne les quantités avant de réserver.
        var besoins = demande.Lignes
            .GroupBy(l => l.ProduitId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantite));

        var articles = (await repository.ObtenirParProduitsAsync(besoins.Keys, ct))
            .ToDictionary(a => a.ProduitId);

        // ÉTAPE 1 : on VÉRIFIE toutes les lignes, sans rien modifier. On garde tous les refus pour un motif complet.
        var refus = new List<string>();
        foreach (var (produitId, quantite) in besoins)
        {
            if (!articles.TryGetValue(produitId, out var article))
                refus.Add($"Le produit {produitId} est inconnu dans le stock.");
            else if (article.VerifierReservation(quantite) is { } raison)
                refus.Add(raison);
        }

        // La trace « message traité » part dans le MÊME SaveChanges que le reste (réservation ou échec) :
        // impossible d'avoir l'une sans l'autre.
        messagesTraites.MarquerCommeTraite(demande.MessageId);

        if (refus.Count > 0)
        {
            // ÉTAPE 2a : refus. Aucun article n'a été modifié ; on dépose seulement le message de compensation
            // dans l'Outbox. Il partira après le commit, vers Commande, qui annulera la commande.
            var motif = string.Join(" ", refus);
            outbox.Ajouter(
                new ReservationEchoueeIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, demande.CommandeId, motif),
                ReservationEchoueeIntegrationEvent.CleRoutage);

            await repository.SauvegarderAsync(ct);

            logger.LogWarning("Réservation refusée pour la commande {CommandeId} : {Motif}", demande.CommandeId, motif);
            return ResultatReservation.Refusee;
        }

        // ÉTAPE 2b : tout est réservable. Les vérifications ont déjà tout validé, Reserver ne peut plus échouer.
        foreach (var (produitId, quantite) in besoins)
            articles[produitId].Reserver(quantite);

        // Un seul SaveChanges : les réservations de toutes les lignes sont enregistrées ensemble, ou pas du tout.
        await repository.SauvegarderAsync(ct);

        logger.LogInformation("Stock réservé pour la commande {CommandeId} : {NombreProduits} produit(s)",
            demande.CommandeId, besoins.Count);
        return ResultatReservation.Reservee;
    }
}
