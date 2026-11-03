using Contrats;

namespace Commande.Application.Abstractions;

// Contrat « déposer un message à envoyer aux autres services ».
// Contrairement à un envoi direct vers RabbitMQ, cette méthode N'ENVOIE RIEN : elle ajoute le message
// à la base de données, dans la même transaction que la modification de la commande.
// Un autre composant (le publicateur) l'enverra ensuite à RabbitMQ.
public interface IOutbox
{
    // cleRoutage : l'« adresse » du message (ex. "commande.confirmee"), utilisée par RabbitMQ pour l'aiguiller.
    void Ajouter<TEvent>(TEvent evenement, string cleRoutage) where TEvent : IIntegrationEvent;
}
