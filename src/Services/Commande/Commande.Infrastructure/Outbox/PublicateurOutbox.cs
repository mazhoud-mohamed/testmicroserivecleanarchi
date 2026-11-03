using Commande.Infrastructure.Messaging;
using Commande.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Commande.Infrastructure.Outbox;

// ÉTAPE 6 du trajet : lit l'Outbox et envoie les messages à RabbitMQ.
//
// Il tourne en boucle en arrière-plan : toutes les 2 secondes, il cherche les messages pas encore envoyés
// (TraiteLe = null), les publie dans l'ordre, puis les marque comme envoyés.
//
// Garantie « au moins une fois » : si l'application plante APRÈS la publication mais AVANT le marquage,
// le message sera renvoyé au redémarrage. Le consommateur peut donc recevoir un doublon (rôle de l'Inbox).
public sealed class PublicateurOutbox(
    IServiceScopeFactory fabriqueScopes,
    IPublicateurMessages publicateur,
    ILogger<PublicateurOutbox> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(2);
    private const int TailleLot = 20;

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(Intervalle);
        try
        {
            do
            {
                try
                {
                    await PublierEnAttenteAsync(arret);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // RabbitMQ ou la base indisponible : les messages restent dans l'Outbox, on réessaie au prochain tour.
                    logger.LogError(ex, "Échec de la publication de l'Outbox, nouvel essai au prochain cycle");
                }
            }
            while (await minuteur.WaitForNextTickAsync(arret));
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal de l'application.
        }
    }

    private async Task PublierEnAttenteAsync(CancellationToken arret)
    {
        // Un scope par cycle : le DbContext est scoped, ce service est un singleton.
        await using var scope = fabriqueScopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommandeDbContext>();

        var messages = await db.MessagesOutbox
            .Where(m => m.TraiteLe == null)
            .OrderBy(m => m.CreeLe)
            .Take(TailleLot)
            .ToListAsync(arret);

        foreach (var message in messages)
        {
            await publicateur.PublierAsync(message.Id, message.Type, message.CleRoutage, message.Contenu, arret);

            // On marque tout de suite après chaque envoi pour limiter les doublons en cas de plantage.
            // Si la publication lève une exception, on sort : les messages suivants attendent, l'ordre est respecté.
            message.TraiteLe = DateTime.UtcNow;
            await db.SaveChangesAsync(arret);

            logger.LogInformation("Message {MessageId} ({Type}) publié depuis l'Outbox", message.Id, message.Type);
        }
    }
}
