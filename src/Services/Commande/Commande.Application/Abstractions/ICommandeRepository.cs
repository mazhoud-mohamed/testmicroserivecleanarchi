using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Abstractions;

// Contrat défini par l'Application, implémenté plus tard par l'Infrastructure.
public interface ICommandeRepository
{
    Task<CommandeAgregat?> ObtenirParIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CommandeAgregat>> ObtenirToutesAsync(CancellationToken ct);
    Task AjouterAsync(CommandeAgregat commande, CancellationToken ct);
    Task SauvegarderAsync(CancellationToken ct);
}
