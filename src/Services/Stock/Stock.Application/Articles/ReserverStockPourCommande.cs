using Microsoft.Extensions.Logging;
using Stock.Application.Abstractions;
using Stock.Domain.Common;

namespace Stock.Application.Articles;

// Types propres à Stock : le cas d'usage ne dépend ni de RabbitMQ ni du format du message de Commande.
// C'est le consommateur (Infrastructure) qui traduit le message en cette demande.
public record LigneAReserver(Guid ProduitId, int Quantite);

public record ReserverStockPourCommande(Guid CommandeId, IReadOnlyList<LigneAReserver> Lignes);

// Réserve le stock de TOUTES les lignes d'une commande, ou d'aucune.
public class ReserverStockPourCommandeHandler(IStockRepository repository, ILogger<ReserverStockPourCommandeHandler> logger)
{
    public async Task Handle(ReserverStockPourCommande demande, CancellationToken ct)
    {
        // Si la commande contient deux fois le même produit, on additionne les quantités avant de réserver.
        var besoins = demande.Lignes
            .GroupBy(l => l.ProduitId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantite));

        var articles = (await repository.ObtenirParProduitsAsync(besoins.Keys, ct))
            .ToDictionary(a => a.ProduitId);

        foreach (var (produitId, quantite) in besoins)
        {
            if (!articles.TryGetValue(produitId, out var article))
                throw new DomainException($"Le produit {produitId} est inconnu dans le stock.");

            // Lève une DomainException si le stock est insuffisant.
            article.Reserver(quantite);
        }

        // Un seul SaveChanges à la fin : si une ligne a échoué plus haut, on n'arrive jamais ici
        // et rien n'est écrit. C'est ce qui rend la réservation « tout ou rien ».
        await repository.SauvegarderAsync(ct);

        logger.LogInformation("Stock réservé pour la commande {CommandeId} : {NombreProduits} produit(s)",
            demande.CommandeId, besoins.Count);
    }
}
