using System.Text.Json;
using Contrats.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stock.Application.Abstractions;
using Stock.Application.Articles;
using Stock.Domain.Articles;
using Stock.Infrastructure.Outbox;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure.Tests;

// Tests d'intégration de la saga côté Stock, sur une vraie base SQL Server.
// Ils prouvent ce que les tests en mémoire ne peuvent pas prouver : les écritures (stock, Inbox, Outbox)
// se font dans UNE SEULE transaction, et la base elle-même protège contre les doublons.
public class SagaStockIntegrationTests : BaseIntegration
{
    private static readonly Guid Clavier = Guid.NewGuid();
    private static readonly Guid Souris = Guid.NewGuid();

    // ---------- Aides ----------

    private Task Approvisionner(Guid produitId, int quantite) => DansUnScope(async sp =>
    {
        await sp.GetRequiredService<IStockRepository>().AjouterAsync(new ArticleStock(produitId, quantite), CancellationToken.None);
        await sp.GetRequiredService<IStockRepository>().SauvegarderAsync(CancellationToken.None);
    });

    // Un message reçu = un scope neuf = un DbContext neuf, comme dans le consommateur.
    private Task<ResultatReservation> Recevoir(Guid messageId, Guid commandeId, params LigneAReserver[] lignes) =>
        DansUnScope(sp => sp.GetRequiredService<ReserverStockPourCommandeHandler>().Handle(
            new ReserverStockPourCommande(messageId, commandeId, lignes), CancellationToken.None));

    private Task<(int Disponible, int Reservee)> Stock(Guid produitId) => DansUnScope(async sp =>
    {
        var a = await sp.GetRequiredService<StockDbContext>().Articles.AsNoTracking().SingleAsync(x => x.ProduitId == produitId);
        return (a.QuantiteDisponible, a.QuantiteReservee);
    });

    private Task<List<MessageOutbox>> LireOutbox() => DansUnScope(sp =>
        sp.GetRequiredService<StockDbContext>().MessagesOutbox.AsNoTracking().OrderBy(m => m.CreeLe).ToListAsync());

    private Task<List<Guid>> LireInbox() => DansUnScope(sp =>
        sp.GetRequiredService<StockDbContext>().MessagesTraites.AsNoTracking().Select(m => m.Id).ToListAsync());

    private Task Sql(string commande) => DansUnScope(sp =>
        sp.GetRequiredService<StockDbContext>().Database.ExecuteSqlRawAsync(commande));

    // ---------- Réservation réussie ----------

    [Fact]
    public async Task Reservation_reussie_ecrit_le_stock_et_l_inbox_mais_aucun_message()
    {
        await Approvisionner(Clavier, 10);
        var messageId = Guid.NewGuid();

        var resultat = await Recevoir(messageId, Guid.NewGuid(), new LigneAReserver(Clavier, 4));

        Assert.Equal(ResultatReservation.Reservee, resultat);
        Assert.Equal((6, 4), await Stock(Clavier));
        Assert.Equal([messageId], await LireInbox());
        Assert.Empty(await LireOutbox());
    }

    // ---------- Refus : la compensation part, le stock ne bouge pas ----------

    [Fact]
    public async Task Stock_insuffisant_laisse_le_stock_intact_et_ecrit_inbox_et_outbox()
    {
        await Approvisionner(Clavier, 3);
        var messageId = Guid.NewGuid();
        var commandeId = Guid.NewGuid();

        var resultat = await Recevoir(messageId, commandeId, new LigneAReserver(Clavier, 500));

        Assert.Equal(ResultatReservation.Refusee, resultat);
        Assert.Equal((3, 0), await Stock(Clavier));
        Assert.Equal([messageId], await LireInbox());

        var message = Assert.Single(await LireOutbox());
        Assert.Equal(ReservationEchoueeIntegrationEvent.CleRoutage, message.CleRoutage);
        Assert.Equal(nameof(ReservationEchoueeIntegrationEvent), message.Type);
        Assert.Null(message.TraiteLe); // pas encore envoyé

        // Le message stocké est exactement ce que Commande saura relire : on le désérialise avec le contrat partagé.
        var evenement = JsonSerializer.Deserialize<ReservationEchoueeIntegrationEvent>(message.Contenu)!;
        Assert.Equal(commandeId, evenement.CommandeId);
        Assert.Equal(message.Id, evenement.EventId);
        Assert.Contains("Stock insuffisant", evenement.Motif);
    }

    [Fact]
    public async Task Produit_inconnu_ecrit_un_message_au_motif_explicite()
    {
        var inconnu = Guid.NewGuid();

        var resultat = await Recevoir(Guid.NewGuid(), Guid.NewGuid(), new LigneAReserver(inconnu, 1));

        Assert.Equal(ResultatReservation.Refusee, resultat);
        var evenement = JsonSerializer.Deserialize<ReservationEchoueeIntegrationEvent>(
            Assert.Single(await LireOutbox()).Contenu)!;
        Assert.Contains(inconnu.ToString(), evenement.Motif);
    }

    [Fact]
    public async Task Tout_ou_rien_en_base_une_ligne_en_rupture_ne_reserve_aucune_ligne()
    {
        await Approvisionner(Clavier, 10);
        await Approvisionner(Souris, 1);

        await Recevoir(Guid.NewGuid(), Guid.NewGuid(), new LigneAReserver(Clavier, 4), new LigneAReserver(Souris, 2));

        Assert.Equal((10, 0), await Stock(Clavier));
        Assert.Equal((1, 0), await Stock(Souris));
    }

    // ---------- Inbox : doublons ----------

    [Fact]
    public async Task Meme_message_recu_deux_fois_ne_reserve_qu_une_fois()
    {
        await Approvisionner(Clavier, 10);
        var messageId = Guid.NewGuid();
        var commandeId = Guid.NewGuid();

        var premier = await Recevoir(messageId, commandeId, new LigneAReserver(Clavier, 4));
        var second = await Recevoir(messageId, commandeId, new LigneAReserver(Clavier, 4));

        Assert.Equal(ResultatReservation.Reservee, premier);
        Assert.Equal(ResultatReservation.DejaTraite, second);
        Assert.Equal((6, 4), await Stock(Clavier));
        Assert.Single(await LireInbox());
    }

    [Fact]
    public async Task Rejeu_d_un_refus_ne_publie_pas_un_second_message()
    {
        await Approvisionner(Clavier, 1);
        var messageId = Guid.NewGuid();

        await Recevoir(messageId, Guid.NewGuid(), new LigneAReserver(Clavier, 9));
        var rejeu = await Recevoir(messageId, Guid.NewGuid(), new LigneAReserver(Clavier, 9));

        Assert.Equal(ResultatReservation.DejaTraite, rejeu);
        Assert.Single(await LireOutbox());
    }

    [Fact]
    public async Task La_base_refuse_elle_meme_deux_fois_le_meme_message_traite()
    {
        // Filet de sécurité de l'Inbox : même si deux traitements passaient le contrôle « déjà traité ? » en
        // même temps, la clé primaire de MessagesTraites interdit la seconde insertion.
        var id = Guid.NewGuid();
        await DansUnScope(async sp =>
        {
            var db = sp.GetRequiredService<StockDbContext>();
            db.MessagesTraites.Add(new MessageTraite { Id = id, TraiteLe = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => DansUnScope(async sp =>
        {
            var db = sp.GetRequiredService<StockDbContext>();
            db.MessagesTraites.Add(new MessageTraite { Id = id, TraiteLe = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }));

        Assert.Single(await LireInbox());
    }

    // ---------- Atomicité : une seule transaction ----------

    [Fact]
    public async Task Atomicite_du_refus_si_l_outbox_echoue_l_inbox_n_est_pas_enregistree()
    {
        await Approvisionner(Clavier, 3);
        // La base REFUSE l'insertion du message de compensation.
        await Sql("ALTER TABLE MessagesOutbox ADD CONSTRAINT CK_RefuseTout CHECK (CleRoutage <> 'stock.reservation-echouee')");
        var messageId = Guid.NewGuid();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Recevoir(messageId, Guid.NewGuid(), new LigneAReserver(Clavier, 500)));

        // Si l'Inbox était écrite à part, le message serait marqué « traité » alors que la compensation n'est jamais
        // partie : la commande resterait confirmée pour toujours. Ici, rien n'est enregistré, le message peut être rejoué.
        Assert.Empty(await LireInbox());
        Assert.Empty(await LireOutbox());
    }

    [Fact]
    public async Task Atomicite_de_la_reservation_si_l_inbox_echoue_le_stock_n_est_pas_reserve()
    {
        await Approvisionner(Clavier, 10);
        // La base REFUSE l'écriture dans l'Inbox.
        await Sql("ALTER TABLE MessagesTraites ADD CONSTRAINT CK_RefuseTout CHECK (1 = 0)");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Recevoir(Guid.NewGuid(), Guid.NewGuid(), new LigneAReserver(Clavier, 4)));

        // Sans transaction commune, le stock serait réservé sans trace : le rejeu réserverait une seconde fois.
        Assert.Equal((10, 0), await Stock(Clavier));
    }

    // ---------- Publicateur : le message de compensation part vraiment ----------

    private async Task AvecPublicateurAsync(Func<Task<bool>> condition, Func<Task>? pendant = null)
    {
        var publicateur = Services.GetServices<IHostedService>().OfType<PublicateurOutbox>().Single();
        await publicateur.StartAsync(CancellationToken.None);
        try
        {
            if (pendant is not null) await pendant();
            var limite = DateTime.UtcNow.AddSeconds(15);
            while (!await condition())
            {
                Assert.True(DateTime.UtcNow < limite, "La condition n'a pas été atteinte en 15 secondes.");
                await Task.Delay(200);
            }
        }
        finally
        {
            await publicateur.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Le_publicateur_envoie_le_refus_avec_la_bonne_cle_puis_le_marque_traite()
    {
        await Approvisionner(Clavier, 1);
        var commandeId = Guid.NewGuid();
        await Recevoir(Guid.NewGuid(), commandeId, new LigneAReserver(Clavier, 9));

        await AvecPublicateurAsync(async () => (await LireOutbox()).All(m => m.TraiteLe is not null));

        var envoye = Assert.Single(Publicateur.Publies);
        Assert.Equal("stock.reservation-echouee", envoye.Cle);
        Assert.Equal(nameof(ReservationEchoueeIntegrationEvent), envoye.Type);
        var evenement = JsonSerializer.Deserialize<ReservationEchoueeIntegrationEvent>(envoye.Contenu)!;
        Assert.Equal(commandeId, evenement.CommandeId);
        Assert.Equal(evenement.EventId, envoye.Id); // MessageId = EventId : c'est ce qui permet l'Inbox côté Commande
    }

    [Fact]
    public async Task Si_rabbitmq_est_en_panne_le_refus_reste_en_attente_puis_part_au_retour()
    {
        await Approvisionner(Clavier, 1);
        await Recevoir(Guid.NewGuid(), Guid.NewGuid(), new LigneAReserver(Clavier, 9));
        Publicateur.EnPanne = true;

        await AvecPublicateurAsync(
            condition: async () => (await LireOutbox()).All(m => m.TraiteLe is not null),
            pendant: async () =>
            {
                // Pendant la panne : plusieurs cycles passent, rien ne doit être marqué ni perdu.
                await Task.Delay(TimeSpan.FromSeconds(5));
                Assert.Null(Assert.Single(await LireOutbox()).TraiteLe);
                Assert.Empty(Publicateur.Publies);

                Publicateur.EnPanne = false; // RabbitMQ revient
            });

        Assert.Single(Publicateur.Publies);
    }
}
