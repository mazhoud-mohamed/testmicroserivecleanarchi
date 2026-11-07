using Contrats.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Stock.Application.Articles;

namespace Stock.Application.Tests;

public class ReserverStockPourCommandeHandlerTests
{
    private static readonly Guid Clavier = Guid.NewGuid();
    private static readonly Guid Souris = Guid.NewGuid();

    private readonly FauxStock _stock = new();
    private readonly ReserverStockPourCommandeHandler _handler;

    public ReserverStockPourCommandeHandlerTests()
    {
        _handler = new ReserverStockPourCommandeHandler(
            _stock, _stock, _stock, NullLogger<ReserverStockPourCommandeHandler>.Instance);
    }

    private static ReserverStockPourCommande Demande(Guid messageId, params LigneAReserver[] lignes) =>
        new(messageId, Guid.NewGuid(), lignes);

    private static ReservationEchoueeIntegrationEvent Echec((string Cle, object Evenement) message) =>
        Assert.IsType<ReservationEchoueeIntegrationEvent>(message.Evenement);

    // ---------- Réservation réussie ----------

    [Fact]
    public async Task Reserve_le_stock_et_marque_le_message_comme_traite()
    {
        _stock.Ajouter(Clavier, 10);
        var messageId = Guid.NewGuid();

        var resultat = await _handler.Handle(Demande(messageId, new LigneAReserver(Clavier, 4)), CancellationToken.None);

        Assert.Equal(ResultatReservation.Reservee, resultat);
        Assert.Equal((6, 4), _stock.EnBase(Clavier));
        Assert.True(_stock.EstTraite(messageId));
        Assert.Empty(_stock.Outbox); // une réussite ne publie rien
    }

    [Fact]
    public async Task Deux_messages_differents_reservent_chacun()
    {
        _stock.Ajouter(Clavier, 10);

        await _handler.Handle(Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 4)), CancellationToken.None);
        await _handler.Handle(Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 3)), CancellationToken.None);

        Assert.Equal((3, 7), _stock.EnBase(Clavier));
    }

    [Fact]
    public async Task Additionne_les_quantites_d_un_meme_produit_present_sur_plusieurs_lignes()
    {
        _stock.Ajouter(Clavier, 10);

        await _handler.Handle(
            Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 3), new LigneAReserver(Clavier, 2)),
            CancellationToken.None);

        Assert.Equal((5, 5), _stock.EnBase(Clavier));
    }

    [Fact]
    public async Task Additionner_les_lignes_peut_depasser_le_stock_meme_si_chaque_ligne_passe_seule()
    {
        _stock.Ajouter(Clavier, 4);

        // 3 puis 3 : chaque ligne tient dans 4, mais le total (6) ne tient pas.
        var resultat = await _handler.Handle(
            Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 3), new LigneAReserver(Clavier, 3)),
            CancellationToken.None);

        Assert.Equal(ResultatReservation.Refusee, resultat);
        Assert.Equal((4, 0), _stock.EnBase(Clavier));
    }

    // ---------- Inbox : doublons ----------

    [Fact]
    public async Task Meme_message_rejoue_ne_reserve_pas_une_seconde_fois()
    {
        _stock.Ajouter(Clavier, 10);
        var demande = Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 4));

        var premier = await _handler.Handle(demande, CancellationToken.None);
        var second = await _handler.Handle(demande, CancellationToken.None); // doublon

        Assert.Equal(ResultatReservation.Reservee, premier);
        Assert.Equal(ResultatReservation.DejaTraite, second);
        Assert.Equal((6, 4), _stock.EnBase(Clavier));
        Assert.Equal(1, _stock.NombreSauvegardes);
    }

    [Fact]
    public async Task Rejeu_d_un_message_refuse_ne_publie_pas_un_second_echec()
    {
        _stock.Ajouter(Clavier, 1);
        var demande = Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 3));

        await _handler.Handle(demande, CancellationToken.None);
        var rejeu = await _handler.Handle(demande, CancellationToken.None);

        Assert.Equal(ResultatReservation.DejaTraite, rejeu);
        Assert.Single(_stock.Outbox); // un seul ReservationEchouee, pas deux
    }

    // ---------- Refus : compensation de la saga ----------

    [Fact]
    public async Task Stock_insuffisant_ne_reserve_rien_et_publie_ReservationEchouee()
    {
        _stock.Ajouter(Clavier, 2);
        var messageId = Guid.NewGuid();
        var demande = Demande(messageId, new LigneAReserver(Clavier, 5));

        var resultat = await _handler.Handle(demande, CancellationToken.None);

        Assert.Equal(ResultatReservation.Refusee, resultat);
        Assert.Equal((2, 0), _stock.EnBase(Clavier));

        var message = Assert.Single(_stock.Outbox);
        Assert.Equal(ReservationEchoueeIntegrationEvent.CleRoutage, message.Cle);
        var evenement = Echec(message);
        Assert.Equal(demande.CommandeId, evenement.CommandeId);
        Assert.Contains("Stock insuffisant", evenement.Motif);
        Assert.NotEqual(Guid.Empty, evenement.EventId);
    }

    [Fact]
    public async Task Produit_inconnu_publie_ReservationEchouee_avec_un_motif_explicite()
    {
        _stock.Ajouter(Clavier, 10);
        var inconnu = Guid.NewGuid();

        var resultat = await _handler.Handle(
            Demande(Guid.NewGuid(), new LigneAReserver(inconnu, 1)), CancellationToken.None);

        Assert.Equal(ResultatReservation.Refusee, resultat);
        var evenement = Echec(Assert.Single(_stock.Outbox));
        Assert.Contains(inconnu.ToString(), evenement.Motif);
        Assert.Contains("inconnu", evenement.Motif);
    }

    [Fact]
    public async Task Tout_ou_rien_une_ligne_en_rupture_n_enregistre_aucune_reservation()
    {
        _stock.Ajouter(Clavier, 10);
        _stock.Ajouter(Souris, 1);

        // Le clavier est disponible, mais pas la souris (2 demandées pour 1 en stock).
        var resultat = await _handler.Handle(
            Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 4), new LigneAReserver(Souris, 2)),
            CancellationToken.None);

        Assert.Equal(ResultatReservation.Refusee, resultat);
        Assert.Equal((10, 0), _stock.EnBase(Clavier));
        Assert.Equal((1, 0), _stock.EnBase(Souris));
    }

    [Fact]
    public async Task Le_motif_rassemble_tous_les_problemes_de_la_commande()
    {
        _stock.Ajouter(Clavier, 1);
        var inconnu = Guid.NewGuid();

        await _handler.Handle(
            Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 5), new LigneAReserver(inconnu, 1)),
            CancellationToken.None);

        var evenement = Echec(Assert.Single(_stock.Outbox));
        Assert.Contains("Stock insuffisant", evenement.Motif);
        Assert.Contains(inconnu.ToString(), evenement.Motif);
    }

    [Fact]
    public async Task Un_refus_marque_le_message_traite_et_l_ecrit_avec_l_outbox_dans_la_meme_sauvegarde()
    {
        _stock.Ajouter(Clavier, 1);
        var messageId = Guid.NewGuid();

        await _handler.Handle(Demande(messageId, new LigneAReserver(Clavier, 3)), CancellationToken.None);

        // Une seule sauvegarde contient à la fois la trace Inbox et le message Outbox : atomique.
        Assert.Equal(1, _stock.NombreSauvegardes);
        Assert.True(_stock.EstTraite(messageId));
        Assert.Single(_stock.Outbox);
    }

    [Fact]
    public async Task Apres_un_refus_le_stock_reste_utilisable_pour_une_autre_commande()
    {
        _stock.Ajouter(Clavier, 5);

        await _handler.Handle(Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 9)), CancellationToken.None);
        _stock.NouveauScope(); // chaque message arrive dans un scope neuf
        var resultat = await _handler.Handle(
            Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 5)), CancellationToken.None);

        Assert.Equal(ResultatReservation.Reservee, resultat);
        Assert.Equal((0, 5), _stock.EnBase(Clavier));
    }
}
