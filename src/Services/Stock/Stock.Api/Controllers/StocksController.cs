using Microsoft.AspNetCore.Mvc;
using Stock.Application.Articles;

namespace Stock.Api.Controllers;

[ApiController]
[Route("api/stocks")]
public class StocksController(ApprovisionnerStockHandler approvisionner, ObtenirStocksHandler obtenirTous)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArticleStockDto>>> ObtenirTous(CancellationToken ct) =>
        Ok(await obtenirTous.Handle(new ObtenirStocks(), ct));

    [HttpPost("approvisionner")]
    public async Task<ActionResult<ArticleStockDto>> Approvisionner(ApprovisionnerStock demande, CancellationToken ct) =>
        Ok(await approvisionner.Handle(demande, ct));
}
