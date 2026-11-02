using Stock.Domain.Articles;

namespace Stock.Application.Abstractions;

public interface IStockRepository
{
    Task<ArticleStock?> ObtenirParProduitAsync(Guid produitId, CancellationToken ct);
    Task<IReadOnlyList<ArticleStock>> ObtenirTousAsync(CancellationToken ct);
    Task AjouterAsync(ArticleStock article, CancellationToken ct);
    Task SauvegarderAsync(CancellationToken ct);
}
