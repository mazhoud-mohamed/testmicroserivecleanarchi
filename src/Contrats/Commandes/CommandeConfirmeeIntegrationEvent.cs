namespace Contrats.Commandes;

// Une ligne du message. Uniquement des types simples : on n'expose JAMAIS un objet du Domain
// dans un contrat, sinon les autres services dépendraient de notre modèle interne.
public record LigneCommandeConfirmee(Guid ProduitId, int Quantite);

// ÉVÉNEMENT D'INTÉGRATION : message PUBLIC envoyé à RabbitMQ pour prévenir les autres services
// (ex. Stock) qu'une commande vient d'être confirmée.
//
// Différence avec le Domain Event « CommandeConfirmee » :
//   - Domain Event      : interne au service Commande, en mémoire, dans la même transaction.
//   - Integration Event : traverse la frontière du service, passe par RabbitMQ, asynchrone.
//
// Ce projet « Contrats » est partagé entre services : toute modification doit rester
// compatible avec ceux qui consomment déjà ce message.
public record CommandeConfirmeeIntegrationEvent(
    Guid EventId,
    DateTime SurvenueLe,
    Guid CommandeId,
    Guid ClientId,
    IReadOnlyList<LigneCommandeConfirmee> Lignes) : IIntegrationEvent
{
    // Clé de routage : RabbitMQ s'en sert pour décider vers quelle(s) file(s) envoyer le message.
    // Un service qui s'abonne à « commande.confirmee » reçoit ces messages-là, et eux seuls.
    public const string CleRoutage = "commande.confirmee";
}
