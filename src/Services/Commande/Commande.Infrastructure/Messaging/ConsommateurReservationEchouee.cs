using System.Diagnostics;
using System.Text.Json;
using Commande.Application.Abstractions;
using Commande.Application.Commandes.Ecriture;
using Commande.Domain.Common;
using Contrats.Stock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Commande.Infrastructure.Messaging;

// CHEMIN RETOUR de la saga : Commande reçoit « la réservation a échoué » publié par Stock.
//
// BackgroundService = un service qui démarre avec l'application et tourne en arrière-plan.
// Singleton (le framework le crée une fois) : il ne peut donc pas recevoir directement le handler ou le DbContext
// (scoped). On ouvre un scope par message avec IServiceScopeFactory, comme ASP.NET le fait pour une requête HTTP.
public sealed class ConsommateurReservationEchouee(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory fabriqueScopes,
    ILogger<ConsommateurReservationEchouee> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        // Si RabbitMQ n'est pas prêt au démarrage, on réessaie au lieu de faire tomber toute l'application.
        while (!arret.IsCancellationRequested)
        {
            try
            {
                await ConsommerAsync(arret);
                return;
            }
            catch (OperationCanceledException) when (arret.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connexion à RabbitMQ impossible, nouvel essai dans 5 secondes");
                await Task.Delay(TimeSpan.FromSeconds(5), arret).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task ConsommerAsync(CancellationToken arret)
    {
        var fabrique = new ConnectionFactory
        {
            HostName = _options.Hote,
            Port = _options.Port,
            UserName = _options.Utilisateur,
            Password = _options.MotDePasse
        };

        await using var connexion = await fabrique.CreateConnectionAsync(arret);
        await using var canal = await connexion.CreateChannelAsync(cancellationToken: arret);

        // 1. L'exchange de Stock (même déclaration que côté Stock : l'ordre de démarrage des services n'a pas d'importance).
        await canal.ExchangeDeclareAsync(_options.ExchangeStock, ExchangeType.Topic, durable: true, cancellationToken: arret);

        // 2. La DEAD-LETTER QUEUE : la file où RabbitMQ range les messages refusés (nack sans requeue), au lieu de les
        // jeter. On pourra les examiner dans l'interface de gestion, les corriger, puis les rejouer à la main.
        // « fanout » : tout message mort est copié dans la file, quelle que soit sa clé de routage d'origine.
        var echangeMorts = $"{_options.FileStock}.dlx";
        var fileMorts = $"{_options.FileStock}.dlq";
        await canal.ExchangeDeclareAsync(echangeMorts, ExchangeType.Fanout, durable: true, cancellationToken: arret);
        await canal.QueueDeclareAsync(fileMorts, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: arret);
        await canal.QueueBindAsync(fileMorts, echangeMorts, routingKey: "", cancellationToken: arret);

        // 3. Notre boîte aux lettres. durable = elle survit à un redémarrage de RabbitMQ.
        // x-dead-letter-exchange : où envoyer un message refusé sans requeue.
        // Attention : ces arguments ne peuvent plus changer une fois la file créée (RabbitMQ refuse la redéclaration).
        await canal.QueueDeclareAsync(_options.FileStock, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = echangeMorts },
            cancellationToken: arret);

        // 4. Le binding : « envoie-moi les messages de cet exchange dont la clé est stock.reservation-echouee ».
        await canal.QueueBindAsync(_options.FileStock, _options.ExchangeStock,
            ReservationEchoueeIntegrationEvent.CleRoutage, cancellationToken: arret);

        // Un seul message non acquitté à la fois : on traite dans l'ordre, sans se faire submerger.
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: arret);

        var consommateur = new AsyncEventingBasicConsumer(canal);
        consommateur.ReceivedAsync += (_, livraison) => TraiterAsync(canal, livraison, arret);

        // autoAck: false = c'est NOUS qui confirmons (ack) quand l'annulation est sauvegardée.
        await canal.BasicConsumeAsync(_options.FileStock, autoAck: false, consommateur, arret);
        logger.LogInformation("Commande écoute la file {File} (clé {Cle})", _options.FileStock,
            ReservationEchoueeIntegrationEvent.CleRoutage);

        // On reste vivant jusqu'à l'arrêt de l'application ; les messages arrivent via ReceivedAsync.
        await Task.Delay(Timeout.Infinite, arret);
    }

    private async Task TraiterAsync(IChannel canal, BasicDeliverEventArgs livraison, CancellationToken arret)
    {
        var messageId = livraison.BasicProperties.MessageId;

        // Un span « consommateur » rattaché à la trace de l'émetteur (Stock) grâce à l'en-tête « traceparent » du message :
        // tout ce que fait ce traitement (SQL, logs) fait donc partie de la MÊME trace que la confirmation d'origine.
        using var activite = Telemetrie.Source.StartActivity(
            $"{_options.FileStock} process", ActivityKind.Consumer,
            Telemetrie.ContexteDepuis(Telemetrie.LireTraceParent(livraison.BasicProperties.Headers)));
        activite?.SetTag("messaging.system", "rabbitmq");
        activite?.SetTag("messaging.destination.name", _options.FileStock);
        activite?.SetTag("messaging.message.id", messageId);

        try
        {
            var evenement = JsonSerializer.Deserialize<ReservationEchoueeIntegrationEvent>(livraison.Body.Span)
                ?? throw new JsonException("Le message est vide.");

            var demande = new AnnulerCommandeSuiteEchecStock(evenement.EventId, evenement.CommandeId, evenement.Motif);

            await using var scope = fabriqueScopes.CreateAsyncScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<IHandler<AnnulerCommandeSuiteEchecStock, ResultatAnnulation>>();
            await handler.Handle(demande, arret);

            // Annulation (ou issue métier définitive) sauvegardée : on confirme à RabbitMQ, qui peut effacer le message.
            await canal.BasicAckAsync(livraison.DeliveryTag, multiple: false, arret);
        }
        catch (Exception ex) when (ex is DomainException or JsonException)
        {
            // Échec DÉFINITIF (message illisible, règle métier violée) : le rejouer donnerait le même résultat.
            // requeue: false = RabbitMQ l'abandonne.
            activite?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "Message {MessageId} rejeté définitivement", messageId);
            await canal.BasicNackAsync(livraison.DeliveryTag, multiple: false, requeue: false, arret);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Échec probablement TEMPORAIRE (base indisponible...) : on remet le message dans la file.
            activite?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Erreur technique sur le message {MessageId}, il sera rejoué", messageId);
            await canal.BasicNackAsync(livraison.DeliveryTag, multiple: false, requeue: true, arret);
        }
    }
}
