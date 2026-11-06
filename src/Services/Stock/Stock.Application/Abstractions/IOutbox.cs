using Contrats;

namespace Stock.Application.Abstractions;

// Contrat « déposer un message à envoyer aux autres services ».
// Cette méthode N'ENVOIE RIEN : elle ajoute le message à la base de données, dans la même transaction
// que la modification du stock. Un autre composant (le publicateur) l'enverra ensuite à RabbitMQ.
public interface IOutbox
{
    // cleRoutage : l'« adresse » du message (ex. "stock.reservation-echouee"), utilisée par RabbitMQ pour l'aiguiller.
    void Ajouter<TEvent>(TEvent evenement, string cleRoutage) where TEvent : IIntegrationEvent;
}
