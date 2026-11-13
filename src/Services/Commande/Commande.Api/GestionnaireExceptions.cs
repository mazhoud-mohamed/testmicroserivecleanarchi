using System.Diagnostics;
using System.Text.Json;
using Commande.Application.Abstractions;
using Commande.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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
            ValidationRequeteException => StatusCodes.Status400BadRequest,
            NonTrouveException => StatusCodes.Status404NotFound,
            DomainException => StatusCodes.Status422UnprocessableEntity,
            _ => 0
        };
        if (status == 0) return false; // exception inattendue : laissée au comportement par défaut (500)

        // Les détails de l'erreur partent aussi dans le span de la requête : ils apparaissent dans les « Tags » de Jaeger.
        // OpenTelemetry n'enregistre jamais le corps de la réponse ; c'est donc à nous d'y mettre ce qui aide à comprendre.
        // Uniquement des messages et des noms de champs, jamais les valeurs saisies par l'utilisateur.
        var activite = Activity.Current;
        activite?.SetTag("erreur.type", ex.GetType().Name);
        activite?.SetTag("erreur.motif", ex.Message);
        if (ex is ValidationRequeteException erreursValidation)
            activite?.SetTag("erreur.details", JsonSerializer.Serialize(erreursValidation.Erreurs));

        // Pas de ex en premier argument : ce n'est pas un bug, on évite la pile d'appels dans les logs.
        ProblemDetails details;
        if (ex is ValidationRequeteException validation)
        {
            // {@Erreurs} : le @ demande à Serilog de sérialiser le dictionnaire (champ -> messages).
            logger.LogWarning("{Methode} {Chemin} refusé ({StatutHttp}) : {Raison} {@Erreurs}",
                ctx.Request.Method, ctx.Request.Path, status, ex.Message, validation.Erreurs);
            details = new ValidationProblemDetails(validation.Erreurs) { Status = status, Title = ex.Message };
        }
        else
        {
            logger.LogWarning("{Methode} {Chemin} refusé ({StatutHttp}) : {Raison}",
                ctx.Request.Method, ctx.Request.Path, status, ex.Message);
            details = new ProblemDetails { Status = status, Title = ex.Message };
        }

        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = details
        });
    }
}
