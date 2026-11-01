using Commande.Application.Abstractions;
using Commande.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Commande.Api;

// Transforme les exceptions métier en réponses HTTP (ProblemDetails) au lieu d'un 500,
// et les journalise ici, à un seul endroit.
public class GestionnaireExceptions(IProblemDetailsService problems, ILogger<GestionnaireExceptions> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var status = ex switch
        {
            NonTrouveException => StatusCodes.Status404NotFound,
            DomainException => StatusCodes.Status422UnprocessableEntity,
            _ => 0
        };
        if (status == 0) return false; // exception inattendue : laissée au comportement par défaut (500)

        // Pas de ex en premier argument : ce n'est pas un bug, on évite la pile d'appels dans les logs.
        logger.LogWarning("{Methode} {Chemin} refusé ({StatutHttp}) : {Raison}",
            ctx.Request.Method, ctx.Request.Path, status, ex.Message);

        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = { Status = status, Title = ex.Message }
        });
    }
}
