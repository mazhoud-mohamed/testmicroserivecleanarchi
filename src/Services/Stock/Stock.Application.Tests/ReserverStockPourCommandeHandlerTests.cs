using Microsoft.Extensions.Logging.Abstractions;
using Stock.Application.Articles;
using Stock.Domain.Common;

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
            _stock, _stock, NullLogger<ReserverStockPourCommandeHandler>.Instance);
    }

    private static ReserverStockPourCommande Demande(Guid messageId, params LigneAReserver[] lignes) =>
        new(messageId, Guid.NewGuid(), lignes);

    [Fact]
    public async Task Reserve_le_stock_et_marque_le_message_comme_traite()
    {
        _stock.Ajouter(Clavier, 10);
        var messageId = Guid.NewGuid();

        await _handler.Handle(Demande(messageId, new LigneAReserver(Clavier, 4)), CancellationToken.None);

        Assert.Equal((6, 4), _stock.EnBase(Clavier));
        Assert.True(_stock.EstTraite(messageId));
    }

    [Fact]
    public async Task Meme_message_rejoue_ne_reserve_pas_une_seconde_fois()
    {
        _stock.Ajouter(Clavier, 10);
        var demande = Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 4));

        await _handler.Handle(demande, CancellationToken.None);
        await _handler.Handle(demande, CancellationToken.None); // doublon

        Assert.Equal((6, 4), _stock.EnBase(Clavier));
        Assert.Equal(1, _stock.NombreSauvegardes);
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
    public async Task Tout_ou_rien_une_ligne_en_rupture_n_enregistre_aucune_reservation()
    {
        _stock.Ajouter(Clavier, 10);
        _stock.Ajouter(Souris, 1);

        // Le clavier est disponible, mais pas la souris (2 demandées pour 1 en stock).
        await Assert.ThrowsAsync<DomainException>(() => _handler.Handle(
            Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 4), new LigneAReserver(Souris, 2)),
            CancellationToken.None));

        Assert.Equal((10, 0), _stock.EnBase(Clavier));
        Assert.Equal((1, 0), _stock.EnBase(Souris));
        Assert.Equal(0, _stock.NombreSauvegardes);
    }

    [Fact]
    public async Task Produit_inconnu_leve_une_exception_et_ne_marque_pas_le_message()
    {
        _stock.Ajouter(Clavier, 10);
        var messageId = Guid.NewGuid();

        await Assert.ThrowsAsync<DomainException>(() => _handler.Handle(
            Demande(messageId, new LigneAReserver(Guid.NewGuid(), 1)),
            CancellationToken.None));

        Assert.False(_stock.EstTraite(messageId));
        Assert.Equal(0, _stock.NombreSauvegardes);
    }

    [Fact]
    public async Task Message_en_echec_n_est_pas_marque_donc_il_peut_etre_rejoue_apres_reapprovisionnement()
    {
        _stock.Ajouter(Clavier, 1);
        var demande = Demande(Guid.NewGuid(), new LigneAReserver(Clavier, 3));

        await Assert.ThrowsAsync<DomainException>(() => _handler.Handle(demande, CancellationToken.None));
        _stock.Approvisionner(Clavier, 5);
        _stock.NouveauScope(); // le rejeu arrive dans un nouveau scope, avec un DbContext neuf
        await _handler.Handle(demande, CancellationToken.None);

        Assert.Equal((3, 3), _stock.EnBase(Clavier));
        Assert.True(_stock.EstTraite(demande.MessageId));
    }
}
