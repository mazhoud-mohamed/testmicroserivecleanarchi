using Commande.Domain.Common;

namespace Commande.Domain.Commandes;

public class Commande
{
    private readonly List<LigneCommande> _lignes = [];

    // Constructeur vide réservé à EF Core.
    private Commande() { }

    public Commande(Guid clientId)
    {
        if (clientId == Guid.Empty) throw new DomainException("Le client est obligatoire.");

        Id = Guid.NewGuid();
        ClientId = clientId;
        Statut = StatutCommande.EnAttente;
        DateCreation = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public StatutCommande Statut { get; private set; }
    public DateTime DateCreation { get; private set; }
    public IReadOnlyCollection<LigneCommande> Lignes => _lignes.AsReadOnly();
    public decimal MontantTotal => _lignes.Sum(l => l.Total);

    public void AjouterLigne(Guid produitId, string nomProduit, decimal prixUnitaire, int quantite)
    {
        VerifierEnAttente();
        _lignes.Add(new LigneCommande(produitId, nomProduit, prixUnitaire, quantite));
    }

    public void Confirmer()
    {
        VerifierEnAttente();
        if (_lignes.Count == 0) throw new DomainException("Impossible de confirmer une commande sans ligne.");
        Statut = StatutCommande.Confirmee;
    }

    public void Expedier()
    {
        if (Statut != StatutCommande.Confirmee)
            throw new DomainException("Seule une commande confirmée peut être expédiée.");
        Statut = StatutCommande.Expediee;
    }

    public void Annuler()
    {
        if (Statut is StatutCommande.Expediee or StatutCommande.Annulee)
            throw new DomainException($"Impossible d'annuler une commande {Statut}.");
        Statut = StatutCommande.Annulee;
    }

    private void VerifierEnAttente()
    {
        if (Statut != StatutCommande.EnAttente)
            throw new DomainException("La commande ne peut plus être modifiée.");
    }
}
