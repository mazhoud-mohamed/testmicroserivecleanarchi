using Commande.Application.Abstractions;

namespace Commande.Application.Commandes.Ecriture;

public enum ActionCommande { Confirmer, Expedier, Annuler }

public record ChangerStatutCommande(Guid CommandeId, ActionCommande Action);

public class ChangerStatutCommandeHandler(ICommandeRepository repository)
    : IHandler<ChangerStatutCommande, CommandeDto>
{
    public async Task<CommandeDto> Handle(ChangerStatutCommande demande, CancellationToken ct)
    {
        var commande = await repository.ObtenirParIdAsync(demande.CommandeId, ct)
            ?? throw new NonTrouveException("Commande", demande.CommandeId);

        switch (demande.Action)
        {
            case ActionCommande.Confirmer: commande.Confirmer(); break;
            case ActionCommande.Expedier: commande.Expedier(); break;
            case ActionCommande.Annuler: commande.Annuler("Annulée à la demande de l'utilisateur."); break;
        }

        await repository.SauvegarderAsync(ct);
        return CommandeDto.Depuis(commande);
    }
}
