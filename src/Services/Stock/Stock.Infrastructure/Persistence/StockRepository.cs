using Microsoft.EntityFrameworkCore;
using Stock.Application.Abstractions;
using Stock.Domain.Articles;

namespace Stock.Infrastructure.Persistence;

public class StockRepository(StockDbContext db) : IStockRepository
{
    public Task<ArticleStock?> ObtenirParProduitAsync(Guid produitId, CancellationToken ct) =>
        db.Articles.FirstOrDefaultAsync(a => a.ProduitId == produitId, ct);

    // Une seule requête pour tous les produits (au lieu d'une par ligne de commande).
    public async Task<IReadOnlyList<ArticleStock>> ObtenirParProduitsAsync(
        IReadOnlyCollection<Guid> produitIds, CancellationToken ct) =>
        await db.Articles.Where(a => produitIds.Contains(a.ProduitId)).ToListAsync(ct);

    public async Task<IReadOnlyList<ArticleStock>> ObtenirTousAsync(CancellationToken ct) =>
        await db.Articles.AsNoTracking().ToListAsync(ct);

    public async Task AjouterAsync(ArticleStock article, CancellationToken ct) =>
        await db.Articles.AddAsync(article, ct);

    public Task SauvegarderAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
