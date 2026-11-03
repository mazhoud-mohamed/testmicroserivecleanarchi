using Stock.Domain.Articles;
using Stock.Domain.Common;

namespace Stock.Domain.Tests;

public class ArticleStockTests
{
    private static readonly Guid ProduitId = Guid.NewGuid();

    [Fact]
    public void Creer_sans_produit_est_refuse()
    {
        Assert.Throws<DomainException>(() => new ArticleStock(Guid.Empty, 10));
    }

    [Fact]
    public void Creer_avec_quantite_negative_est_refuse()
    {
        Assert.Throws<DomainException>(() => new ArticleStock(ProduitId, -1));
    }

    [Fact]
    public void Nouvel_article_n_a_rien_de_reserve()
    {
        var article = new ArticleStock(ProduitId, 10);

        Assert.Equal(10, article.QuantiteDisponible);
        Assert.Equal(0, article.QuantiteReservee);
    }

    [Fact]
    public void Approvisionner_augmente_le_disponible()
    {
        var article = new ArticleStock(ProduitId, 10);

        article.Approvisionner(5);

        Assert.Equal(15, article.QuantiteDisponible);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Approvisionner_avec_quantite_invalide_est_refuse(int quantite)
    {
        var article = new ArticleStock(ProduitId, 10);

        Assert.Throws<DomainException>(() => article.Approvisionner(quantite));
        Assert.Equal(10, article.QuantiteDisponible);
    }

    [Fact]
    public void Reserver_deplace_du_disponible_vers_le_reserve()
    {
        var article = new ArticleStock(ProduitId, 10);

        article.Reserver(4);

        Assert.Equal(6, article.QuantiteDisponible);
        Assert.Equal(4, article.QuantiteReservee);
    }

    [Fact]
    public void Reserver_tout_le_disponible_est_autorise()
    {
        var article = new ArticleStock(ProduitId, 10);

        article.Reserver(10);

        Assert.Equal(0, article.QuantiteDisponible);
        Assert.Equal(10, article.QuantiteReservee);
    }

    [Fact]
    public void Reserver_plus_que_le_disponible_est_refuse_sans_rien_modifier()
    {
        var article = new ArticleStock(ProduitId, 10);

        Assert.Throws<DomainException>(() => article.Reserver(11));

        Assert.Equal(10, article.QuantiteDisponible);
        Assert.Equal(0, article.QuantiteReservee);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Reserver_avec_quantite_invalide_est_refuse(int quantite)
    {
        var article = new ArticleStock(ProduitId, 10);

        Assert.Throws<DomainException>(() => article.Reserver(quantite));
    }

    [Fact]
    public void Reservations_successives_s_additionnent()
    {
        var article = new ArticleStock(ProduitId, 10);

        article.Reserver(3);
        article.Reserver(2);

        Assert.Equal(5, article.QuantiteDisponible);
        Assert.Equal(5, article.QuantiteReservee);
    }
}
