using Commande.Application.Abstractions;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Tests;

// Faux dépôt en mémoire : permet de tester les handlers sans SQL Server.
public class FauxDepotCommandes : ICommandeRepository
{
    private readonly Dictionary<Guid, CommandeAgregat> _commandes = [];

    public int NombreSauvegardes { get; private set; }
    public IReadOnlyCollection<CommandeAgregat> Commandes => _commandes.Values;

    public Task<CommandeAgregat?> ObtenirParIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_commandes.GetValueOrDefault(id));

    public Task<IReadOnlyList<CommandeAgregat>> ObtenirToutesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CommandeAgregat>>(_commandes.Values.ToList());

    public Task AjouterAsync(CommandeAgregat commande, CancellationToken ct)
    {
        _commandes[commande.Id] = commande;
        return Task.CompletedTask;
    }

    public Task SauvegarderAsync(CancellationToken ct)
    {
        NombreSauvegardes++;
        return Task.CompletedTask;
    }
}
