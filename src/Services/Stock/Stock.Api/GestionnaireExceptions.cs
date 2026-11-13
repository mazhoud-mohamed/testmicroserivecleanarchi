using System.Diagnostics;
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

        // Les détails de l'erreur partent aussi dans le span de la requête : ils apparaissent dans les « Tags » de Jaeger.
        // OpenTelemetry n'enregistre jamais le corps de la réponse ; c'est donc à nous d'y mettre ce qui aide à comprendre.
        var activite = Activity.Current;
        activite?.SetTag("erreur.type", ex.GetType().Name);
        activite?.SetTag("erreur.motif", titre);

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
