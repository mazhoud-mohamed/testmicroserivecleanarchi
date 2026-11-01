using Commande.Application.Abstractions;

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

public class ObtenirCommandesHandler(ICommandeRepository repository)
{
    public async Task<IReadOnlyList<CommandeDto>> Handle(ObtenirCommandes demande, CancellationToken ct)
    {
        var commandes = await repository.ObtenirToutesAsync(ct);
        return commandes.Select(CommandeDto.Depuis).ToList();
    }
}
