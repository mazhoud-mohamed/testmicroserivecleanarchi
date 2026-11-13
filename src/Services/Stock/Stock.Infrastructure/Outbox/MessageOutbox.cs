namespace Stock.Infrastructure.Outbox;

// Une ligne de la table MessagesOutbox = un message à envoyer (ou déjà envoyé).
public class MessageOutbox
{
    // Même valeur que l'EventId du message : le consommateur pourra s'en servir pour détecter un doublon.
    public Guid Id { get; set; }

    public string Type { get; set; } = "";       // ex. "ReservationEchoueeIntegrationEvent"
    public string CleRoutage { get; set; } = ""; // ex. "stock.reservation-echouee"
    public string Contenu { get; set; } = "";    // le message sérialisé en JSON
    public DateTime CreeLe { get; set; }

    // null = pas encore envoyé à RabbitMQ.
    public DateTime? TraiteLe { get; set; }

    // Contexte de trace de l'opération qui a écrit ce message (format W3C « traceparent »).
    // Le message est envoyé plus tard, par un service d'arrière-plan sans contexte : sans cette colonne,
    // la trace serait coupée ici. null si le message a été écrit hors de toute trace.
    public string? TraceParent { get; set; }
}
