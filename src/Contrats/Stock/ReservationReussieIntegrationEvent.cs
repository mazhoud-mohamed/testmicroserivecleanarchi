namespace Contrats.Stock;

// ÉVÉNEMENT D'INTÉGRATION publié par Stock quand il a réussi à réserver le stock d'une commande.
//
// C'est le pendant de ReservationEchoueeIntegrationEvent : avec les deux, Commande sait TOUJOURS où en est la réservation.
// Sans lui, une commande « Confirmee » pouvait vouloir dire « stock réservé » ou « Stock n'a pas encore répondu »,
// et on risquait d'expédier une commande dont la réservation allait échouer.
//
// Aucun objet du Domain ici, seulement des types simples : Commande ne doit rien savoir du modèle de Stock.
public record ReservationReussieIntegrationEvent(
    Guid EventId,
    DateTime SurvenueLe,
    Guid CommandeId) : IIntegrationEvent
{
    // Clé de routage sur l'exchange de Stock (« stock.events ») : Commande s'abonne à celle-ci.
    public const string CleRoutage = "stock.reservation-reussie";
}
