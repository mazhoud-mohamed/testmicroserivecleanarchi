using System.Text.Json;
using Contrats.Commandes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Stock.Application.Articles;
using Stock.Domain.Common;

namespace Stock.Infrastructure.Messaging;

// ÉTAPE 8 du trajet : Stock reçoit le message publié par Commande.
//
// BackgroundService = un service qui démarre avec l'application et tourne en arrière-plan,
// sans requête HTTP. C'est lui qui « écoute » la boîte aux lettres de Stock.
//
// Singleton (le framework le crée une fois) : il ne peut donc pas recevoir directement le handler ou le DbContext
// (scoped). On ouvre un scope par message avec IServiceScopeFactory, comme le fait ASP.NET pour chaque requête HTTP.
public sealed class ConsommateurCommandeConfirmee(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory fabriqueScopes,
    ILogger<ConsommateurCommandeConfirmee> logger) : BackgroundService
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

        // 1. L'exchange (même déclaration que côté Commande : l'ordre de démarrage des services n'a pas d'importance).
        await canal.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: arret);

        // 2. Notre boîte aux lettres. durable = elle survit à un redémarrage de RabbitMQ.
        await canal.QueueDeclareAsync(_options.File, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: arret);

        // 3. Le binding : « envoie-moi les messages de cet exchange dont la clé est commande.confirmee ».
        await canal.QueueBindAsync(_options.File, _options.Exchange, CommandeConfirmeeIntegrationEvent.CleRoutage,
            cancellationToken: arret);

        // Un seul message non acquitté à la fois : on traite dans l'ordre, sans se faire submerger.
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: arret);

        var consommateur = new AsyncEventingBasicConsumer(canal);
        consommateur.ReceivedAsync += (_, livraison) => TraiterAsync(canal, livraison, arret);

        // autoAck: false = c'est NOUS qui confirmons (ack) quand la réservation est sauvegardée.
        await canal.BasicConsumeAsync(_options.File, autoAck: false, consommateur, arret);
        logger.LogInformation("Stock écoute la file {File} (clé {Cle})", _options.File,
            CommandeConfirmeeIntegrationEvent.CleRoutage);

        // On reste vivant jusqu'à l'arrêt de l'application ; les messages arrivent via ReceivedAsync.
        await Task.Delay(Timeout.Infinite, arret);
    }

    private async Task TraiterAsync(IChannel canal, BasicDeliverEventArgs livraison, CancellationToken arret)
    {
        var messageId = livraison.BasicProperties.MessageId;
        try
        {
            var evenement = JsonSerializer.Deserialize<CommandeConfirmeeIntegrationEvent>(livraison.Body.Span)
                ?? throw new JsonException("Le message est vide.");

            // Traduction du message de Commande en demande propre à Stock.
            var demande = new ReserverStockPourCommande(
                evenement.EventId,
                evenement.CommandeId,
                evenement.Lignes.Select(l => new LigneAReserver(l.ProduitId, l.Quantite)).ToList());

            await using var scope = fabriqueScopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ReserverStockPourCommandeHandler>();
            await handler.Handle(demande, arret);

            // Réservation sauvegardée : on confirme à RabbitMQ, qui peut effacer le message.
            await canal.BasicAckAsync(livraison.DeliveryTag, multiple: false, arret);
        }
        catch (Exception ex) when (ex is DomainException or JsonException)
        {
            // Échec DÉFINITIF (stock insuffisant, produit inconnu, message illisible) :
            // le rejouer donnerait le même résultat. requeue: false = RabbitMQ l'abandonne.
            logger.LogWarning(ex, "Message {MessageId} rejeté définitivement", messageId);
            await canal.BasicNackAsync(livraison.DeliveryTag, multiple: false, requeue: false, arret);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Échec probablement TEMPORAIRE (base indisponible...) : on remet le message dans la file.
            logger.LogError(ex, "Erreur technique sur le message {MessageId}, il sera rejoué", messageId);
            await canal.BasicNackAsync(livraison.DeliveryTag, multiple: false, requeue: true, arret);
        }
    }
}
