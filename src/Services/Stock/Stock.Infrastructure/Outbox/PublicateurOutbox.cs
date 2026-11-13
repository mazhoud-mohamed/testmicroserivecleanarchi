using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stock.Infrastructure.Messaging;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure.Outbox;

// Lit l'Outbox de Stock et envoie les messages à RabbitMQ.
//
// Il tourne en boucle en arrière-plan : toutes les 2 secondes, il cherche les messages pas encore envoyés
// (TraiteLe = null), les publie dans l'ordre, puis les marque comme envoyés.
//
// Garantie « au moins une fois » : si l'application plante APRÈS la publication mais AVANT le marquage,
// le message sera renvoyé au redémarrage. Le consommateur (Commande) peut donc recevoir un doublon :
// c'est le rôle de son Inbox.
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
                    Telemetrie.EchecDePublication();
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
        var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();

        // Combien de messages attendent ? Mesuré à chaque cycle (la requête est peu coûteuse grâce à l'index sur TraiteLe).
        var enAttente = await db.MessagesOutbox.CountAsync(m => m.TraiteLe == null, arret);
        Telemetrie.DefinirEnAttente(enAttente);

        var messages = await db.MessagesOutbox
            .Where(m => m.TraiteLe == null)
            .OrderBy(m => m.CreeLe)
            .Take(TailleLot)
            .ToListAsync(arret);

        foreach (var message in messages)
        {
            // Un span « producteur » rattaché à la trace d'ORIGINE (celle du traitement qui a écrit le message) :
            // c'est lui qui recolle les deux morceaux. RabbitMqPublicateur le propage dans les en-têtes du message.
            using var activite = Telemetrie.Source.StartActivity(
                $"outbox publish {message.Type}", ActivityKind.Producer, Telemetrie.ContexteDepuis(message.TraceParent));
            activite?.SetTag("messaging.system", "rabbitmq");
            activite?.SetTag("messaging.message.id", message.Id.ToString());

            await publicateur.PublierAsync(message.Id, message.Type, message.CleRoutage, message.Contenu, arret);

            // On marque tout de suite après chaque envoi pour limiter les doublons en cas de plantage.
            // Si la publication lève une exception, on sort : les messages suivants attendent, l'ordre est respecté.
            message.TraiteLe = DateTime.UtcNow;
            await db.SaveChangesAsync(arret);

            logger.LogInformation("Message {MessageId} ({Type}) publié depuis l'Outbox", message.Id, message.Type);

            Telemetrie.MessagePublie(message.Type);
            Telemetrie.DefinirEnAttente(--enAttente);
        }
    }
}
