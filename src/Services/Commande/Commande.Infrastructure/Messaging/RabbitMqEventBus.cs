using System.Text.Json;
using Commande.Application.Abstractions;
using Contrats;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Commande.Infrastructure.Messaging;

// ÉTAPE 5 du trajet : envoie réellement le message à RabbitMQ.
//
// Vocabulaire RabbitMQ :
//   - Exchange : le « bureau de tri ». On y dépose le message avec une clé de routage.
//   - Queue    : la « boîte aux lettres » d'un service. Elle est liée (binding) à l'exchange.
//   - Le message va de l'exchange vers les queues dont le binding correspond à la clé de routage.
//   - Le publieur (nous) ne connaît jamais les queues ni les consommateurs.
//
// Singleton : une seule connexion TCP partagée ; un canal (léger) est ouvert à chaque publication.
public sealed class RabbitMqEventBus(IOptions<RabbitMqOptions> options) : IEventBus, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _verrou = new(1, 1); // évite deux connexions créées en même temps
    private IConnection? _connexion;

    public async Task PublierAsync<TEvent>(TEvent evenement, string cleRoutage, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        var connexion = await ObtenirConnexionAsync(ct);

        // Un canal n'est pas prévu pour être partagé entre threads : on en ouvre un par publication.
        await using var canal = await connexion.CreateChannelAsync(cancellationToken: ct);

        // Idempotent : crée l'exchange s'il n'existe pas, ne fait rien sinon.
        // topic = routage par clé (ex. "commande.confirmee"), durable = survit à un redémarrage de RabbitMQ.
        await canal.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);

        // Le message voyage sous forme d'octets : ici le JSON de l'événement.
        var corps = JsonSerializer.SerializeToUtf8Bytes(evenement);
        var proprietes = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent, // le message est écrit sur disque par RabbitMQ
            MessageId = evenement.EventId.ToString(), // permet au consommateur de reconnaître un doublon
            Type = typeof(TEvent).Name                // indique au consommateur quel type désérialiser
        };

        // Dépôt dans l'exchange. Si aucune queue n'est liée à cette clé, le message est jeté :
        // c'est le consommateur (Stock) qui doit déclarer sa queue et son binding.
        await canal.BasicPublishAsync(_options.Exchange, cleRoutage, mandatory: false, proprietes, corps, ct);
    }

    private async Task<IConnection> ObtenirConnexionAsync(CancellationToken ct)
    {
        if (_connexion is { IsOpen: true }) return _connexion;

        await _verrou.WaitAsync(ct);
        try
        {
            // Deuxième vérification : un autre appel a pu créer la connexion pendant qu'on attendait le verrou.
            if (_connexion is { IsOpen: true }) return _connexion;

            var fabrique = new ConnectionFactory
            {
                HostName = _options.Hote,
                Port = _options.Port,
                UserName = _options.Utilisateur,
                Password = _options.MotDePasse
            };
            _connexion = await fabrique.CreateConnectionAsync(ct);
            return _connexion;
        }
        finally
        {
            _verrou.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connexion is not null) await _connexion.DisposeAsync();
        _verrou.Dispose();
    }
}
