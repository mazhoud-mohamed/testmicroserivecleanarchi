using Commande.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Commande.Application.Commandes.Lecture;

public record ObtenirCommandeParId(Guid CommandeId);
public record ObtenirCommandes;

public class ObtenirCommandeParIdHandler(ICommandeRepository repository)
{
    public async Task<CommandeDto> Handle(ObtenirCommandeParId demande, CancellationToken ct)
    {
        var commande = await repository.ObtenirParIdAsync(demande.CommandeId, ct)
            ?? throw new NonTrouveException("Commande", demande.CommandeId);
        return CommandeDto.Depuis(commande);
    }
}

public class ObtenirCommandesHandler(ICommandeRepository repository, ILogger<ObtenirCommandesHandler> logger)
{
    public async Task<IReadOnlyList<CommandeDto>> Handle(ObtenirCommandes demande, CancellationToken ct)
    {
        logger.LogInformation("Chargement des commandes");
        var commandes = await repository.ObtenirToutesAsync(ct);
        logger.LogInformation("Chargement terminé : {NombreCommandes} commande(s)", commandes.Count);
        return commandes.Select(CommandeDto.Depuis).ToList();
    }
}
