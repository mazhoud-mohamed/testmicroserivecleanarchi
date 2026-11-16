namespace Commande.Domain.Commandes;

// Le statut est enregistré en texte (HasConversion<string>) : les valeurs numériques ne sont jamais stockées,
// on peut donc ajouter un statut sans toucher aux lignes existantes.
public enum StatutCommande
{
    EnAttente = 0,
    Confirmee = 1,     // confirmée par le client ; Stock n'a pas encore répondu
    Expediee = 2,
    Annulee = 3,
    StockReserve = 4   // Stock a confirmé la réservation : la commande peut être expédiée
}
