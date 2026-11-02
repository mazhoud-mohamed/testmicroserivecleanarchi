using Stock.Domain.Articles;

namespace Stock.Application.Articles;

public record ArticleStockDto(Guid ProduitId, int QuantiteDisponible, int QuantiteReservee)
{
    public static ArticleStockDto Depuis(ArticleStock a) => new(a.ProduitId, a.QuantiteDisponible, a.QuantiteReservee);
}
