using Microsoft.AspNetCore.Diagnostics;
using Stock.Domain.Common;

namespace Stock.Api;

// Transforme une règle métier violée en 422 et la journalise ; le reste retombe sur le 500 par défaut.
public class GestionnaireExceptions(IProblemDetailsService problems, ILogger<GestionnaireExceptions> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not DomainException) return false;

        logger.LogWarning("{Methode} {Chemin} refusé ({StatutHttp}) : {Raison}",
            ctx.Request.Method, ctx.Request.Path, StatusCodes.Status422UnprocessableEntity, ex.Message);

        ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = { Status = StatusCodes.Status422UnprocessableEntity, Title = ex.Message }
        });
    }
}
