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

    // Réserver = retirer du disponible et mettre de côté pour une commande.
    public void Reserver(int quantite)
    {
        if (quantite <= 0) throw new DomainException("La quantité à réserver doit être supérieure à zéro.");
        if (quantite > QuantiteDisponible)
            throw new DomainException(
                $"Stock insuffisant pour le produit {ProduitId} : {QuantiteDisponible} disponible(s), {quantite} demandé(s).");

        QuantiteDisponible -= quantite;
        QuantiteReservee += quantite;
    }
}
