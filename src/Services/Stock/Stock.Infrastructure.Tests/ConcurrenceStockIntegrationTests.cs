using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stock.Application.Abstractions;
using Stock.Domain.Articles;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure.Tests;

// Concurrence optimiste : deux traitements lisent le MÊME article en même temps, puis le modifient chacun de leur côté.
// Sans protection, le second écrase le premier (« mise à jour perdue ») : du stock disparaît sans laisser de trace.
// Avec RowVersion, la base détecte que la ligne a changé entre la lecture et l'écriture, et refuse la seconde.
public class ConcurrenceStockIntegrationTests : BaseIntegration
{
    private static readonly Guid Clavier = Guid.NewGuid();

    private Task Approvisionner(int quantite) => DansUnScope(async sp =>
    {
        var depot = sp.GetRequiredService<IStockRepository>();
        await depot.AjouterAsync(new ArticleStock(Clavier, quantite), CancellationToken.None);
        await depot.SauvegarderAsync(CancellationToken.None);
    });

    private Task<(int Disponible, int Reservee)> Stock() => DansUnScope(async sp =>
    {
        var a = await sp.GetRequiredService<StockDbContext>().Articles.AsNoTracking().SingleAsync(x => x.ProduitId == Clavier);
        return (a.QuantiteDisponible, a.QuantiteReservee);
    });

    // Deux « traitements » en parallèle = deux scopes = deux DbContext, chacun avec sa propre copie de l'article.
    private async Task<(IStockRepository Depot, ArticleStock Article, IAsyncDisposable Scope)> Ouvrir()
    {
        var scope = Services.CreateAsyncScope();
        var depot = scope.ServiceProvider.GetRequiredService<IStockRepository>();
        var article = (await depot.ObtenirParProduitAsync(Clavier, CancellationToken.None))!;
        return (depot, article, scope);
    }

    [Fact]
    public async Task Deux_reservations_simultanees_la_seconde_est_refusee_au_lieu_d_ecraser_la_premiere()
    {
        await Approvisionner(10);
        var (depotA, articleA, scopeA) = await Ouvrir();
        var (depotB, articleB, scopeB) = await Ouvrir();
        await using (scopeA) await using (scopeB)
        {
            // Les deux ont lu « 10 disponibles ».
            articleA.Reserver(4);
            await depotA.SauvegarderAsync(CancellationToken.None);

            articleB.Reserver(4); // B ignore que A est passé avant lui
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => depotB.SauvegarderAsync(CancellationToken.None));
        }

        // Seule la réservation de A est enregistrée. Sans RowVersion, B aurait écrit (6, 4) par-dessus (6, 4) :
        // 8 unités auraient été promises à des commandes, mais le stock n'en compterait que 4 de réservées.
        Assert.Equal((6, 4), await Stock());
    }

    [Fact]
    public async Task Un_approvisionnement_ne_se_perd_pas_pendant_une_reservation()
    {
        await Approvisionner(10);
        var (depotA, articleA, scopeA) = await Ouvrir();
        await using (scopeA)
        {
            // A lit « 10 disponibles ». Pendant ce temps, un approvisionnement de +5 est enregistré.
            await DansUnScope(async sp =>
            {
                var depot = sp.GetRequiredService<IStockRepository>();
                var article = (await depot.ObtenirParProduitAsync(Clavier, CancellationToken.None))!;
                article.Approvisionner(5);
                await depot.SauvegarderAsync(CancellationToken.None);
            });

            articleA.Reserver(4); // A calcule 10 - 4 = 6 à partir de sa lecture périmée
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => depotA.SauvegarderAsync(CancellationToken.None));
        }

        // Sans RowVersion, A écrirait 6 et effacerait les +5 : on aurait perdu 5 unités de stock.
        Assert.Equal((15, 0), await Stock());
    }

    [Fact]
    public async Task Apres_un_conflit_le_rejeu_relit_le_stock_a_jour_et_aboutit()
    {
        await Approvisionner(10);
        var (depotA, articleA, scopeA) = await Ouvrir();
        var (depotB, articleB, scopeB) = await Ouvrir();
        await using (scopeA) await using (scopeB)
        {
            articleA.Reserver(4);
            await depotA.SauvegarderAsync(CancellationToken.None);
            articleB.Reserver(4);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => depotB.SauvegarderAsync(CancellationToken.None));
        }

        // Le consommateur remet le message dans la file (requeue) : le rejeu part d'un scope neuf et relit (6, 4).
        await DansUnScope(async sp =>
        {
            var depot = sp.GetRequiredService<IStockRepository>();
            var article = (await depot.ObtenirParProduitAsync(Clavier, CancellationToken.None))!;
            article.Reserver(4);
            await depot.SauvegarderAsync(CancellationToken.None);
        });

        // Les deux réservations sont comptées : 10 - 4 - 4 = 2 disponibles, 8 réservées.
        Assert.Equal((2, 8), await Stock());
    }
}
