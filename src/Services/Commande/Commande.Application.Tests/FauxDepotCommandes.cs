using Commande.Application.Abstractions;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Tests;

// Faux dépôt en mémoire : permet de tester les handlers sans SQL Server.
// Il joue aussi le rôle d'Inbox (IMessagesTraites) : comme avec le vrai DbContext, la trace d'un message
// n'est enregistrée qu'au SauvegarderAsync, dans la même « transaction » que la commande.
public class FauxDepotCommandes : ICommandeRepository, IMessagesTraites
{
    private readonly Dictionary<Guid, CommandeAgregat> _commandes = [];
    private readonly HashSet<Guid> _messagesTraites = [];
    private readonly List<Guid> _marquesEnAttente = [];

    public bool EstTraite(Guid messageId) => _messagesTraites.Contains(messageId);

    public Task<bool> ADejaEteTraiteAsync(Guid messageId, CancellationToken ct) =>
        Task.FromResult(_messagesTraites.Contains(messageId));

    public void MarquerCommeTraite(Guid messageId) => _marquesEnAttente.Add(messageId);

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
        _messagesTraites.UnionWith(_marquesEnAttente);
        _marquesEnAttente.Clear();
        return Task.CompletedTask;
    }
}
