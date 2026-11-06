using Stock.Application.Abstractions;
using Stock.Domain.Articles;

namespace Stock.Application.Tests;

// Fausse base de données en mémoire, pour tester le handler sans SQL Server.
// Elle imite le vrai DbContext sur deux points essentiels :
//   - les articles lus sont des COPIES « suivies » : les modifier ne change pas la base tant qu'on n'a pas sauvegardé ;
//   - MarquerCommeTraite n'est enregistré qu'au SauvegarderAsync, dans la même « transaction » que les articles.
public class FauxStock : IStockRepository, IMessagesTraites
{
    // Ce qui est réellement « en base ».
    private readonly Dictionary<Guid, (int Disponible, int Reservee)> _enBase = [];
    private readonly HashSet<Guid> _messagesTraites = [];

    // Ce que le « DbContext » suit en ce moment (vidé à chaque sauvegarde réussie).
    private readonly Dictionary<Guid, ArticleStock> _suivis = [];
    private readonly List<Guid> _marquesEnAttente = [];

    public int NombreSauvegardes { get; private set; }

    public void Ajouter(Guid produitId, int quantiteDisponible) => _enBase[produitId] = (quantiteDisponible, 0);

    public void Approvisionner(Guid produitId, int quantite) =>
        _enBase[produitId] = (_enBase[produitId].Disponible + quantite, _enBase[produitId].Reservee);

    // Lecture de l'état enregistré (et non de l'objet en mémoire).
    public (int Disponible, int Reservee) EnBase(Guid produitId) => _enBase[produitId];

    public bool EstTraite(Guid messageId) => _messagesTraites.Contains(messageId);

    // Le consommateur ouvre un scope (donc un DbContext neuf) par message : ce qui était suivi ou
    // en attente d'une sauvegarde qui n'a pas eu lieu est abandonné.
    public void NouveauScope()
    {
        _suivis.Clear();
        _marquesEnAttente.Clear();
    }

    // IStockRepository
    public Task<ArticleStock?> ObtenirParProduitAsync(Guid produitId, CancellationToken ct) =>
        Task.FromResult(_enBase.ContainsKey(produitId) ? Suivre(produitId) : null);

    public Task<IReadOnlyList<ArticleStock>> ObtenirParProduitsAsync(IReadOnlyCollection<Guid> produitIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ArticleStock>>(
            produitIds.Where(_enBase.ContainsKey).Select(Suivre).ToList());

    public Task<IReadOnlyList<ArticleStock>> ObtenirTousAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ArticleStock>>(_enBase.Keys.Select(Suivre).ToList());

    public Task AjouterAsync(ArticleStock article, CancellationToken ct)
    {
        _suivis[article.ProduitId] = article;
        return Task.CompletedTask;
    }

    public Task SauvegarderAsync(CancellationToken ct)
    {
        NombreSauvegardes++;
        foreach (var article in _suivis.Values)
            _enBase[article.ProduitId] = (article.QuantiteDisponible, article.QuantiteReservee);
        _messagesTraites.UnionWith(_marquesEnAttente);

        _suivis.Clear();
        _marquesEnAttente.Clear();
        return Task.CompletedTask;
    }

    // IMessagesTraites
    public Task<bool> ADejaEteTraiteAsync(Guid messageId, CancellationToken ct) =>
        Task.FromResult(_messagesTraites.Contains(messageId));

    public void MarquerCommeTraite(Guid messageId) => _marquesEnAttente.Add(messageId);

    // Construit une copie de l'article tel qu'il est en base, comme le fait EF Core à la lecture.
    private ArticleStock Suivre(Guid produitId)
    {
        if (_suivis.TryGetValue(produitId, out var dejaSuivi)) return dejaSuivi;

        var (disponible, reservee) = _enBase[produitId];
        var article = new ArticleStock(produitId, disponible + reservee);
        if (reservee > 0) article.Reserver(reservee);
        _suivis[produitId] = article;
        return article;
    }
}
