using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Commandes;

public record LigneCommandeDto(Guid ProduitId, string NomProduit, decimal PrixUnitaire, int Quantite, decimal Total);

public record CommandeDto(
    Guid Id,
    Guid ClientId,
    string Statut,
    DateTime DateCreation,
    decimal MontantTotal,
    IReadOnlyList<LigneCommandeDto> Lignes)
{
    public static CommandeDto Depuis(CommandeAgregat c) => new(
        c.Id,
        c.ClientId,
        c.Statut.ToString(),
        c.DateCreation,
        c.MontantTotal,
        c.Lignes.Select(l => new LigneCommandeDto(l.ProduitId, l.NomProduit, l.PrixUnitaire, l.Quantite, l.Total)).ToList());
}
