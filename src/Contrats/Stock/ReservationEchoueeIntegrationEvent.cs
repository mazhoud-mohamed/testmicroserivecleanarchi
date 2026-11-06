namespace Contrats.Stock;

// ÉVÉNEMENT D'INTÉGRATION publié par Stock quand il n'a PAS pu réserver le stock d'une commande
// (produit inconnu, stock insuffisant...). C'est le message de COMPENSATION de la saga :
// il permet au service Commande de défaire ce qu'il avait confirmé trop tôt.
//
// Le chemin aller est : Commande confirme -> « commande.confirmee » -> Stock tente de réserver.
// Le chemin retour (ce message) est : Stock échoue -> « stock.reservation-echouee » -> Commande annule.
//
// Aucun objet du Domain ici, seulement des types simples : Commande ne doit rien savoir du modèle de Stock.
public record ReservationEchoueeIntegrationEvent(
    Guid EventId,
    DateTime SurvenueLe,
    Guid CommandeId,
    // Raison lisible, recopiée telle quelle dans la commande annulée (ex. « Stock insuffisant pour le produit … »).
    string Motif) : IIntegrationEvent
{
    // Clé de routage sur l'exchange de Stock (« stock.events ») : Commande s'abonne à celle-ci.
    public const string CleRoutage = "stock.reservation-echouee";
}
