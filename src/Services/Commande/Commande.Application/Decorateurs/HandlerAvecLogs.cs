using System.Diagnostics;
using Commande.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Commande.Application.Decorateurs;

// Enveloppe n'importe quel handler : journalise le début, la fin et la durée.
// Les exceptions ne sont pas journalisées ici : elles remontent au gestionnaire global de l'API.
public class HandlerAvecLogs<TRequete, TReponse>(
    IHandler<TRequete, TReponse> inner,
    ILogger<HandlerAvecLogs<TRequete, TReponse>> logger) : IHandler<TRequete, TReponse>
{
    public async Task<TReponse> Handle(TRequete requete, CancellationToken ct)
    {
        var nom = typeof(TRequete).Name;
        logger.LogDebug("Début du traitement de {Requete}", nom);

        var chrono = Stopwatch.StartNew();
        var reponse = await inner.Handle(requete, ct);

        logger.LogInformation("{Requete} traité en {DureeMs} ms", nom, chrono.ElapsedMilliseconds);
        return reponse;
    }
}
