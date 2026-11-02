namespace Contrats;

// Tout message public échangé entre microservices implémente cette interface.
// Elle impose deux informations que chaque message doit porter.
public interface IIntegrationEvent
{
    // Identifiant unique du message. Si RabbitMQ le livre deux fois, le consommateur
    // reconnaît le doublon grâce à cet id et ne traite pas deux fois (pattern Inbox).
    Guid EventId { get; }

    // Moment où le fait métier s'est produit (pas le moment où le message est lu).
    DateTime SurvenueLe { get; }
}
