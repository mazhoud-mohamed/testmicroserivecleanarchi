using Commande.Application.Abstractions;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Commandes.Ecriture;

public record LigneACreer(Guid ProduitId, string NomProduit, decimal PrixUnitaire, int Quantite);

public record CreerCommande(Guid ClientId, IReadOnlyList<LigneACreer> Lignes);

public class CreerCommandeHandler(ICommandeRepository repository)
{
    public async Task<CommandeDto> Handle(CreerCommande demande, CancellationToken ct)
    {
        var commande = new CommandeAgregat(demande.ClientId);
        foreach (var l in demande.Lignes)
            commande.AjouterLigne(l.ProduitId, l.NomProduit, l.PrixUnitaire, l.Quantite);

        await repository.AjouterAsync(commande, ct);
        await repository.SauvegarderAsync(ct);
        return CommandeDto.Depuis(commande);
    }
}
