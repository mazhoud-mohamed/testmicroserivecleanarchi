using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Stock.Domain.Common;

namespace Stock.Api;

// Transforme une règle métier violée en 422 et un conflit de concurrence en 409, et les journalise ;
// le reste retombe sur le 500 par défaut.
public class GestionnaireExceptions(IProblemDetailsService problems, ILogger<GestionnaireExceptions> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, titre) = ex switch
        {
            DomainException => (StatusCodes.Status422UnprocessableEntity, ex.Message),
            // Le stock a été modifié par quelqu'un d'autre pendant notre traitement : rien n'a été enregistré,
            // le client peut simplement renvoyer la requête.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "Le stock a été modifié en même temps par une autre opération. Réessayez."),
            _ => (0, "")
        };
        if (status == 0) return false;

        logger.LogWarning("{Methode} {Chemin} refusé ({StatutHttp}) : {Raison}",
            ctx.Request.Method, ctx.Request.Path, status, titre);

        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = { Status = status, Title = titre }
        });
    }
}
