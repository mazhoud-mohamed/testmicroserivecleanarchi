using Stock.Domain.Common;

namespace Stock.Domain.Articles;

// Le stock d'UN produit. Le ProduitId est le même identifiant que celui porté par les messages
// du service Commande (LigneCommandeConfirmee.ProduitId) : c'est le lien entre les deux services.
public class ArticleStock
{
    // Constructeur vide réservé à EF Core.
    private ArticleStock() { }

    public ArticleStock(Guid produitId, int quantiteDisponible)
    {
        if (produitId == Guid.Empty) throw new DomainException("Le produit est obligatoire.");
        if (quantiteDisponible < 0) throw new DomainException("La quantité disponible ne peut pas être négative.");

        ProduitId = produitId;
        QuantiteDisponible = quantiteDisponible;
    }

    public Guid ProduitId { get; private set; }
    public int QuantiteDisponible { get; private set; }
    public int QuantiteReservee { get; private set; }

    public void Approvisionner(int quantite)
    {
        if (quantite <= 0) throw new DomainException("La quantité à ajouter doit être supérieure à zéro.");
        QuantiteDisponible += quantite;
    }

    // Vérifie si on peut réserver cette quantité, SANS rien modifier.
    // Renvoie null si c'est possible, sinon la raison du refus.
    // Permet à l'Application de contrôler toutes les lignes d'une commande avant de toucher au stock.
    public string? VerifierReservation(int quantite)
    {
        if (quantite <= 0) return "La quantité à réserver doit être supérieure à zéro.";
        if (quantite > QuantiteDisponible)
            return $"Stock insuffisant pour le produit {ProduitId} : {QuantiteDisponible} disponible(s), {quantite} demandé(s).";
        return null;
    }

    // Réserver = retirer du disponible et mettre de côté pour une commande.
    public void Reserver(int quantite)
    {
        // Même règle que VerifierReservation : une seule source de vérité.
        var refus = VerifierReservation(quantite);
        if (refus is not null) throw new DomainException(refus);

        QuantiteDisponible -= quantite;
        QuantiteReservee += quantite;
    }
}
