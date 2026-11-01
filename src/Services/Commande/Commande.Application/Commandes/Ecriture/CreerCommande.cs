using Commande.Application.Abstractions;
using Microsoft.Extensions.Logging;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Commandes.Ecriture;

public record LigneACreer(Guid ProduitId, string NomProduit, decimal PrixUnitaire, int Quantite);

public record CreerCommande(Guid ClientId, IReadOnlyList<LigneACreer> Lignes);

public class CreerCommandeHandler(ICommandeRepository repository, ILogger<CreerCommandeHandler> logger)
    : IHandler<CreerCommande, CommandeDto>
{
    public async Task<CommandeDto> Handle(CreerCommande demande, CancellationToken ct)
    {
        var commande = new CommandeAgregat(demande.ClientId);
        foreach (var l in demande.Lignes)
            commande.AjouterLigne(l.ProduitId, l.NomProduit, l.PrixUnitaire, l.Quantite);

        await repository.AjouterAsync(commande, ct);
        await repository.SauvegarderAsync(ct);

        // Log métier : l'identifiant créé, que le décorateur ne peut pas connaître.
        logger.LogInformation("Commande {CommandeId} créée pour le client {ClientId}, montant total {MontantTotal}",
            commande.Id, commande.ClientId, commande.MontantTotal);
        return CommandeDto.Depuis(commande);
    }
}
