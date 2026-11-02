using Stock.Application.Abstractions;

namespace Stock.Application.Articles;

public record ObtenirStocks;

public class ObtenirStocksHandler(IStockRepository repository)
{
    public async Task<IReadOnlyList<ArticleStockDto>> Handle(ObtenirStocks demande, CancellationToken ct)
    {
        var articles = await repository.ObtenirTousAsync(ct);
        return articles.Select(ArticleStockDto.Depuis).ToList();
    }
}
