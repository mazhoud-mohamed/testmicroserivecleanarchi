using Commande.Application.Commandes.Ecriture;
using Commande.Domain.Commandes;
using Microsoft.Extensions.Logging.Abstractions;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Application.Tests;

// Chemin « aller » de la saga, issue favorable : Stock a réservé, la commande devient expédiable.
public class MarquerStockReserveHandlerTests
{
    private readonly FauxDepotCommandes _depot = new();
    private readonly MarquerStockReserveHandler _handler;

    public MarquerStockReserveHandlerTests()
    {
        _handler = new MarquerStockReserveHandler(
            _depot, _depot, NullLogger<MarquerStockReserveHandler>.Instance);
    }

    private async Task<CommandeAgregat> CommandeConfirmeeAsync()
    {
        var commande = new CommandeAgregat(Guid.NewGuid());
        commande.AjouterLigne(Guid.NewGuid(), "Clavier", 50m, 2);
        commande.Confirmer();
        await _depot.AjouterAsync(commande, CancellationToken.None);
        return commande;
    }

    private Task<ResultatReservationStock> Traiter(Guid messageId, Guid commandeId) =>
        _handler.Handle(new MarquerStockReserve(messageId, commandeId), CancellationToken.None);

    [Fact]
    public async Task Une_commande_confirmee_passe_a_StockReserve_et_le_message_est_marque_traite()
    {
        var commande = await CommandeConfirmeeAsync();
        var messageId = Guid.NewGuid();

        var resultat = await Traiter(messageId, commande.Id);

        Assert.Equal(ResultatReservationStock.StockReserve, resultat);
        Assert.Equal(StatutCommande.StockReserve, commande.Statut);
        Assert.True(_depot.EstTraite(messageId));
        Assert.Equal(1, _depot.NombreSauvegardes);
    }

    [Fact]
    public async Task La_commande_devient_expediable_seulement_apres_ce_message()
    {
        var commande = await CommandeConfirmeeAsync();
        Assert.Throws<Commande.Domain.Common.DomainException>(commande.Expedier); // avant : refusé

        await Traiter(Guid.NewGuid(), commande.Id);

        commande.Expedier(); // après : accepté
        Assert.Equal(StatutCommande.Expediee, commande.Statut);
    }

    [Fact]
    public async Task Meme_message_rejoue_est_ignore()
    {
        var commande = await CommandeConfirmeeAsync();
        var messageId = Guid.NewGuid();
        await Traiter(messageId, commande.Id);

        var rejeu = await Traiter(messageId, commande.Id);

        Assert.Equal(ResultatReservationStock.DejaTraite, rejeu);
        Assert.Equal(1, _depot.NombreSauvegardes);
    }

    [Fact]
    public async Task Un_second_message_pour_une_commande_deja_StockReserve_est_acquitte_sans_erreur()
    {
        var commande = await CommandeConfirmeeAsync();
        await Traiter(Guid.NewGuid(), commande.Id);
        var secondId = Guid.NewGuid(); // un AUTRE message (autre EventId) pour la même commande

        var resultat = await Traiter(secondId, commande.Id);

        Assert.Equal(ResultatReservationStock.DejaReserve, resultat);
        Assert.Equal(StatutCommande.StockReserve, commande.Statut);
        Assert.True(_depot.EstTraite(secondId));
    }

    [Fact]
    public async Task Commande_annulee_entre_temps_n_est_pas_modifiee_mais_le_message_est_acquitte()
    {
        var commande = await CommandeConfirmeeAsync();
        commande.Annuler("Annulée à la demande de l'utilisateur.");
        var messageId = Guid.NewGuid();

        var resultat = await Traiter(messageId, commande.Id);

        // Le stock est réservé pour une commande annulée : le handler le signale (log d'alerte), il ne peut rien libérer.
        Assert.Equal(ResultatReservationStock.CommandeAnnulee, resultat);
        Assert.Equal(StatutCommande.Annulee, commande.Statut);
        Assert.True(_depot.EstTraite(messageId)); // sinon rejoué indéfiniment sans pouvoir aboutir
    }

    [Fact]
    public async Task Commande_deja_expediee_est_acquittee_sans_changement()
    {
        var commande = await CommandeConfirmeeAsync();
        commande.MarquerStockReserve();
        commande.Expedier();

        var resultat = await Traiter(Guid.NewGuid(), commande.Id);

        Assert.Equal(ResultatReservationStock.StatutInattendu, resultat);
        Assert.Equal(StatutCommande.Expediee, commande.Statut);
    }

    [Fact]
    public async Task Commande_introuvable_est_acquittee_et_l_inbox_enregistree()
    {
        var messageId = Guid.NewGuid();

        var resultat = await Traiter(messageId, Guid.NewGuid());

        Assert.Equal(ResultatReservationStock.CommandeIntrouvable, resultat);
        Assert.True(_depot.EstTraite(messageId));
    }

    [Fact]
    public async Task Une_reussite_pour_une_commande_ne_touche_pas_les_autres()
    {
        var premiere = await CommandeConfirmeeAsync();
        var seconde = await CommandeConfirmeeAsync();

        await Traiter(Guid.NewGuid(), premiere.Id);

        Assert.Equal(StatutCommande.StockReserve, premiere.Statut);
        Assert.Equal(StatutCommande.Confirmee, seconde.Statut);
    }
}
