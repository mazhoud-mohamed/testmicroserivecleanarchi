namespace Commande.Infrastructure.Outbox;

// Une ligne de la table MessagesOutbox = un message à envoyer (ou déjà envoyé).
public class MessageOutbox
{
    // Même valeur que l'EventId du message : le consommateur pourra s'en servir pour détecter un doublon.
    public Guid Id { get; set; }

    public string Type { get; set; } = "";       // ex. "CommandeConfirmeeIntegrationEvent"
    public string CleRoutage { get; set; } = ""; // ex. "commande.confirmee"
    public string Contenu { get; set; } = "";    // le message sérialisé en JSON
    public DateTime CreeLe { get; set; }

    // null = pas encore envoyé à RabbitMQ.
    public DateTime? TraiteLe { get; set; }
}
