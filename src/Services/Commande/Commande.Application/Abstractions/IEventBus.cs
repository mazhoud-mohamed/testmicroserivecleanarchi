using Contrats;

namespace Commande.Application.Abstractions;

// Contrat « publier un message vers les autres services ».
// L'Application sait QU'elle publie, mais pas COMMENT : c'est l'Infrastructure (RabbitMQ) qui le fait.
// Changer de broker (Kafka, Azure Service Bus...) ne toucherait donc que l'Infrastructure.
public interface IEventBus
{
    // cleRoutage : l'« adresse » du message (ex. "commande.confirmee"), utilisée par RabbitMQ pour l'aiguiller.
    Task PublierAsync<TEvent>(TEvent evenement, string cleRoutage, CancellationToken ct)
        where TEvent : IIntegrationEvent;
}
