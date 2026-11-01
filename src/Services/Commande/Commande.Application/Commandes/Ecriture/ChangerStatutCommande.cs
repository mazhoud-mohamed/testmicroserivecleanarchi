using Commande.Application.Abstractions;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Commandes.Ecriture;

public enum ActionCommande { Confirmer, Expedier, Annuler }

public record ChangerStatutCommande(Guid CommandeId, ActionCommande Action);

public class ChangerStatutCommandeHandler(ICommandeRepository repository)
{
    public async Task<CommandeDto> Handle(ChangerStatutCommande demande, CancellationToken ct)
    {
        var commande = await repository.ObtenirParIdAsync(demande.CommandeId, ct)
            ?? throw new NonTrouveException(nameof(CommandeAgregat), demande.CommandeId);

        switch (demande.Action)
        {
            case ActionCommande.Confirmer: commande.Confirmer(); break;
            case ActionCommande.Expedier: commande.Expedier(); break;
            case ActionCommande.Annuler: commande.Annuler(); break;
        }

        await repository.SauvegarderAsync(ct);
        return CommandeDto.Depuis(commande);
    }
}
