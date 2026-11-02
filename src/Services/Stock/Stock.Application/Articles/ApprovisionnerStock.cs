using Stock.Application.Abstractions;
using Stock.Domain.Articles;

namespace Stock.Application.Articles;

public record ApprovisionnerStock(Guid ProduitId, int Quantite);

// Ajoute du stock à un produit (le crée s'il n'existe pas encore).
// Sert surtout à préparer des données pour tester la réservation de l'étape 5c.
public class ApprovisionnerStockHandler(IStockRepository repository)
{
    public async Task<ArticleStockDto> Handle(ApprovisionnerStock demande, CancellationToken ct)
    {
        var article = await repository.ObtenirParProduitAsync(demande.ProduitId, ct);
        if (article is null)
        {
            article = new ArticleStock(demande.ProduitId, 0);
            await repository.AjouterAsync(article, ct);
        }

        article.Approvisionner(demande.Quantite);
        await repository.SauvegarderAsync(ct);
        return ArticleStockDto.Depuis(article);
    }
}
