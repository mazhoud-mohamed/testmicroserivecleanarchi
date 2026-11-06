using Commande.Application.Abstractions;
using Commande.Application.Commandes.Ecriture;
using Commande.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Tests;

public class CreerCommandeHandlerTests
{
    private readonly FauxDepotCommandes _depot = new();
    private readonly CreerCommandeHandler _handler;

    public CreerCommandeHandlerTests()
    {
        _handler = new CreerCommandeHandler(_depot, NullLogger<CreerCommandeHandler>.Instance);
    }

    [Fact]
    public async Task Cree_et_sauvegarde_la_commande_et_renvoie_son_dto()
    {
        var clientId = Guid.NewGuid();
        var produitId = Guid.NewGuid();
        var demande = new CreerCommande(clientId, [new LigneACreer(produitId, "Clavier", 50m, 2)]);

        var dto = await _handler.Handle(demande, CancellationToken.None);

        var commande = Assert.Single(_depot.Commandes);
        Assert.Equal(1, _depot.NombreSauvegardes);
        Assert.Equal(commande.Id, dto.Id);
        Assert.Equal(clientId, dto.ClientId);
        Assert.Equal("EnAttente", dto.Statut);
        Assert.Equal(100m, dto.MontantTotal);
        var ligne = Assert.Single(dto.Lignes);
        Assert.Equal(produitId, ligne.ProduitId);
    }

    [Fact]
    public async Task Une_ligne_invalide_n_enregistre_rien()
    {
        // Le validateur ne tourne pas ici (il est dans un décorateur) : c'est le Domain qui protège en dernier recours.
        var demande = new CreerCommande(Guid.NewGuid(), [new LigneACreer(Guid.NewGuid(), "Clavier", 50m, 0)]);

        await Assert.ThrowsAsync<DomainException>(() => _handler.Handle(demande, CancellationToken.None));

        Assert.Empty(_depot.Commandes);
        Assert.Equal(0, _depot.NombreSauvegardes);
    }
}

public class ChangerStatutCommandeHandlerTests
{
    private readonly FauxDepotCommandes _depot = new();
    private readonly ChangerStatutCommandeHandler _handler;

    public ChangerStatutCommandeHandlerTests()
    {
        _handler = new ChangerStatutCommandeHandler(_depot);
    }

    private async Task<CommandeAgregat> UneCommandeAvecLigneAsync()
    {
        var commande = new CommandeAgregat(Guid.NewGuid());
        commande.AjouterLigne(Guid.NewGuid(), "Clavier", 50m, 2);
        await _depot.AjouterAsync(commande, CancellationToken.None);
        return commande;
    }

    [Fact]
    public async Task Commande_inconnue_leve_NonTrouveException()
    {
        var demande = new ChangerStatutCommande(Guid.NewGuid(), ActionCommande.Confirmer);

        await Assert.ThrowsAsync<NonTrouveException>(() => _handler.Handle(demande, CancellationToken.None));
        Assert.Equal(0, _depot.NombreSauvegardes);
    }

    [Fact]
    public async Task Confirmer_change_le_statut_et_sauvegarde()
    {
        var commande = await UneCommandeAvecLigneAsync();

        var dto = await _handler.Handle(
            new ChangerStatutCommande(commande.Id, ActionCommande.Confirmer), CancellationToken.None);

        Assert.Equal("Confirmee", dto.Statut);
        Assert.Equal(1, _depot.NombreSauvegardes);
    }

    [Fact]
    public async Task Expedier_avant_confirmation_leve_DomainException_sans_sauvegarder()
    {
        var commande = await UneCommandeAvecLigneAsync();

        await Assert.ThrowsAsync<DomainException>(() => _handler.Handle(
            new ChangerStatutCommande(commande.Id, ActionCommande.Expedier), CancellationToken.None));

        Assert.Equal(0, _depot.NombreSauvegardes);
    }

    [Fact]
    public async Task Annuler_une_commande_en_attente_la_passe_a_Annulee()
    {
        var commande = await UneCommandeAvecLigneAsync();

        var dto = await _handler.Handle(
            new ChangerStatutCommande(commande.Id, ActionCommande.Annuler), CancellationToken.None);

        Assert.Equal("Annulee", dto.Statut);
    }
}
