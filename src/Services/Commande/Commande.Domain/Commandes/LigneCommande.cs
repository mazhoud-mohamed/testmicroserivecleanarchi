using Commande.Domain.Common;

namespace Commande.Domain.Commandes;

public class LigneCommande
{
    // Constructeur vide réservé à EF Core (matérialisation depuis la base).
    private LigneCommande() { }

    // internal : seule la Commande (même assembly) peut créer une ligne.
    internal LigneCommande(Guid produitId, string nomProduit, decimal prixUnitaire, int quantite)
    {
        if (string.IsNullOrWhiteSpace(nomProduit)) throw new DomainException("Le nom du produit est obligatoire.");
        if (prixUnitaire < 0) throw new DomainException("Le prix unitaire ne peut pas être négatif.");
        if (quantite <= 0) throw new DomainException("La quantité doit être supérieure à zéro.");

        Id = Guid.NewGuid();
        ProduitId = produitId;
        NomProduit = nomProduit;
        PrixUnitaire = prixUnitaire;
        Quantite = quantite;
    }

    public Guid Id { get; private set; }
    public Guid ProduitId { get; private set; }
    public string NomProduit { get; private set; } = string.Empty;
    public decimal PrixUnitaire { get; private set; }
    public int Quantite { get; private set; }
    public decimal Total => PrixUnitaire * Quantite;
}
