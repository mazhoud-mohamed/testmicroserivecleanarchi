using Commande.Application.Commandes.Ecriture;
using Commande.Domain.Commandes;
using Microsoft.Extensions.Logging.Abstractions;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Tests;

// Le chemin retour de la saga : Stock a annoncé « ReservationEchouee », Commande défait la confirmation.
public class AnnulerCommandeSuiteEchecStockHandlerTests
{
    private const string Motif = "Stock insuffisant pour le produit X : 2 disponible(s), 5 demandé(s).";

    private readonly FauxDepotCommandes _depot = new();
    private readonly AnnulerCommandeSuiteEchecStockHandler _handler;

    public AnnulerCommandeSuiteEchecStockHandlerTests()
    {
        _handler = new AnnulerCommandeSuiteEchecStockHandler(
            _depot, _depot, NullLogger<AnnulerCommandeSuiteEchecStockHandler>.Instance);
    }

    private async Task<CommandeAgregat> CommandeConfirmeeAsync()
    {
        var commande = new CommandeAgregat(Guid.NewGuid());
        commande.AjouterLigne(Guid.NewGuid(), "Clavier", 50m, 2);
        commande.Confirmer();
        await _depot.AjouterAsync(commande, CancellationToken.None);
        return commande;
    }

    private Task<ResultatAnnulation> Traiter(Guid messageId, Guid commandeId) =>
        _handler.Handle(new AnnulerCommandeSuiteEchecStock(messageId, commandeId, Motif), CancellationToken.None);

    [Fact]
    public async Task Annule_la_commande_confirmee_avec_le_motif_et_marque_le_message_traite()
    {
        var commande = await CommandeConfirmeeAsync();
        var messageId = Guid.NewGuid();

        var resultat = await Traiter(messageId, commande.Id);

        Assert.Equal(ResultatAnnulation.Annulee, resultat);
        Assert.Equal(StatutCommande.Annulee, commande.Statut);
        Assert.Equal(Motif, commande.MotifAnnulation);
        Assert.True(_depot.EstTraite(messageId));
        Assert.Equal(1, _depot.NombreSauvegardes);
    }

    [Fact]
    public async Task Meme_message_rejoue_n_annule_pas_une_seconde_fois()
    {
        var commande = await CommandeConfirmeeAsync();
        var messageId = Guid.NewGuid();
        await Traiter(messageId, commande.Id);

        var rejeu = await Traiter(messageId, commande.Id);

        Assert.Equal(ResultatAnnulation.DejaTraite, rejeu);
        Assert.Equal(1, _depot.NombreSauvegardes);
    }

    [Fact]
    public async Task Commande_deja_annulee_par_le_client_n_est_pas_modifiee()
    {
        var commande = await CommandeConfirmeeAsync();
        commande.Annuler("Annulée à la demande de l'utilisateur.");

        var resultat = await Traiter(Guid.NewGuid(), commande.Id);

        Assert.Equal(ResultatAnnulation.DejaAnnulee, resultat);
        // Le premier motif est conservé : l'échec de stock n'écrase pas la raison réelle.
        Assert.Equal("Annulée à la demande de l'utilisateur.", commande.MotifAnnulation);
    }

    [Fact]
    public async Task Commande_deja_expediee_n_est_pas_annulee_mais_le_message_est_acquitte()
    {
        var commande = await CommandeConfirmeeAsync();
        commande.Expedier();
        var messageId = Guid.NewGuid();

        var resultat = await Traiter(messageId, commande.Id);

        Assert.Equal(ResultatAnnulation.NonAnnulable, resultat);
        Assert.Equal(StatutCommande.Expediee, commande.Statut);
        Assert.Null(commande.MotifAnnulation);
        // Marqué traité : sans cela le message serait rejoué indéfiniment sans jamais pouvoir aboutir.
        Assert.True(_depot.EstTraite(messageId));
    }

    [Fact]
    public async Task Commande_introuvable_est_acquittee_sans_erreur()
    {
        var messageId = Guid.NewGuid();

        var resultat = await Traiter(messageId, Guid.NewGuid());

        Assert.Equal(ResultatAnnulation.CommandeIntrouvable, resultat);
        Assert.True(_depot.EstTraite(messageId));
    }

    [Fact]
    public async Task Deux_echecs_pour_deux_commandes_annulent_chacune_la_leur()
    {
        var premiere = await CommandeConfirmeeAsync();
        var seconde = await CommandeConfirmeeAsync();

        await Traiter(Guid.NewGuid(), premiere.Id);

        Assert.Equal(StatutCommande.Annulee, premiere.Statut);
        Assert.Equal(StatutCommande.Confirmee, seconde.Statut);
    }
}
