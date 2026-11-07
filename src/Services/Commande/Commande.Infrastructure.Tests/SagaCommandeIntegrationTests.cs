using Commande.Application.Abstractions;
using Commande.Application.Commandes;
using Commande.Application.Commandes.Ecriture;
using Commande.Infrastructure.Outbox;
using Commande.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Commande.Infrastructure.Tests;

// Tests d'intégration du chemin retour de la saga côté Commande, sur une vraie base SQL Server :
// Stock a annoncé « la réservation a échoué », Commande doit annuler la commande, une seule fois, de façon atomique.
public class SagaCommandeIntegrationTests : BaseIntegration
{
    private const string Motif = "Stock insuffisant pour le produit X : 3 disponible(s), 500 demandé(s).";

    // ---------- Aides ----------

    private Task<CommandeDto> Creer() => DansUnScope(sp =>
        sp.GetRequiredService<IHandler<CreerCommande, CommandeDto>>().Handle(
            new CreerCommande(Guid.NewGuid(), [new LigneACreer(Guid.NewGuid(), "Clavier", 50m, 2)]),
            CancellationToken.None));

    private Task<CommandeDto> Changer(Guid id, ActionCommande action) => DansUnScope(sp =>
        sp.GetRequiredService<IHandler<ChangerStatutCommande, CommandeDto>>().Handle(
            new ChangerStatutCommande(id, action), CancellationToken.None));

    private async Task<Guid> CommandeConfirmee()
    {
        var commande = await Creer();
        await Changer(commande.Id, ActionCommande.Confirmer);
        return commande.Id;
    }

    // Un message reçu = un scope neuf = un DbContext neuf, comme dans le consommateur.
    private Task<ResultatAnnulation> Recevoir(Guid messageId, Guid commandeId, string motif = Motif) =>
        DansUnScope(sp => sp.GetRequiredService<IHandler<AnnulerCommandeSuiteEchecStock, ResultatAnnulation>>().Handle(
            new AnnulerCommandeSuiteEchecStock(messageId, commandeId, motif), CancellationToken.None));

    private Task<(string Statut, string? Motif)> Etat(Guid id) => DansUnScope(async sp =>
    {
        var c = await sp.GetRequiredService<CommandeDbContext>().Commandes.AsNoTracking().SingleAsync(x => x.Id == id);
        return (c.Statut.ToString(), c.MotifAnnulation);
    });

    private Task<List<Guid>> LireInbox() => DansUnScope(sp =>
        sp.GetRequiredService<CommandeDbContext>().MessagesTraites.AsNoTracking().Select(m => m.Id).ToListAsync());

    private Task<List<MessageOutbox>> LireOutbox() => DansUnScope(sp =>
        sp.GetRequiredService<CommandeDbContext>().MessagesOutbox.AsNoTracking().ToListAsync());

    private Task Sql(string commande) => DansUnScope(sp =>
        sp.GetRequiredService<CommandeDbContext>().Database.ExecuteSqlRawAsync(commande));

    // ---------- Annulation ----------

    [Fact]
    public async Task Un_echec_de_stock_annule_la_commande_confirmee_et_ecrit_l_inbox()
    {
        var id = await CommandeConfirmee();
        var messageId = Guid.NewGuid();

        var resultat = await Recevoir(messageId, id);

        Assert.Equal(ResultatAnnulation.Annulee, resultat);
        Assert.Equal(("Annulee", Motif), await Etat(id));
        Assert.Equal([messageId], await LireInbox());
    }

    [Fact]
    public async Task L_annulation_n_ecrit_aucun_nouveau_message_dans_l_outbox()
    {
        var id = await CommandeConfirmee();
        Assert.Single(await LireOutbox()); // le message « commande.confirmee » de la confirmation

        await Recevoir(Guid.NewGuid(), id);

        // La compensation ne relance pas la saga : pas de nouveau message sortant.
        Assert.Single(await LireOutbox());
    }

    // ---------- Inbox : doublons ----------

    [Fact]
    public async Task Meme_message_recu_deux_fois_n_annule_qu_une_fois()
    {
        var id = await CommandeConfirmee();
        var messageId = Guid.NewGuid();

        var premier = await Recevoir(messageId, id);
        var second = await Recevoir(messageId, id);

        Assert.Equal(ResultatAnnulation.Annulee, premier);
        Assert.Equal(ResultatAnnulation.DejaTraite, second);
        Assert.Single(await LireInbox());
    }

    [Fact]
    public async Task La_base_refuse_elle_meme_deux_fois_le_meme_message_traite()
    {
        // Filet de sécurité de l'Inbox : la clé primaire de MessagesTraites interdit la seconde insertion,
        // même si deux traitements passaient le contrôle « déjà traité ? » en même temps.
        var id = Guid.NewGuid();
        await DansUnScope(async sp =>
        {
            var db = sp.GetRequiredService<CommandeDbContext>();
            db.MessagesTraites.Add(new MessageTraite { Id = id, TraiteLe = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => DansUnScope(async sp =>
        {
            var db = sp.GetRequiredService<CommandeDbContext>();
            db.MessagesTraites.Add(new MessageTraite { Id = id, TraiteLe = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }));

        Assert.Single(await LireInbox());
    }

    // ---------- Cas limites ----------

    [Fact]
    public async Task Une_commande_deja_annulee_par_le_client_garde_son_motif()
    {
        var id = await CommandeConfirmee();
        await Changer(id, ActionCommande.Annuler); // annulation par le client (motif par défaut)

        var resultat = await Recevoir(Guid.NewGuid(), id);

        Assert.Equal(ResultatAnnulation.DejaAnnulee, resultat);
        var (statut, motif) = await Etat(id);
        Assert.Equal("Annulee", statut);
        Assert.NotEqual(Motif, motif); // l'échec de stock n'écrase pas la vraie raison
    }

    [Fact]
    public async Task Une_commande_deja_expediee_n_est_pas_annulee_mais_le_message_est_marque_traite()
    {
        var id = await CommandeConfirmee();
        await Changer(id, ActionCommande.Expedier);
        var messageId = Guid.NewGuid();

        var resultat = await Recevoir(messageId, id);

        Assert.Equal(ResultatAnnulation.NonAnnulable, resultat);
        Assert.Equal(("Expediee", null), await Etat(id));
        // Marqué traité : sinon le message serait rejoué indéfiniment sans jamais pouvoir aboutir.
        Assert.Equal([messageId], await LireInbox());
    }

    [Fact]
    public async Task Une_commande_introuvable_est_acquittee_et_l_inbox_enregistree()
    {
        var messageId = Guid.NewGuid();

        var resultat = await Recevoir(messageId, Guid.NewGuid());

        Assert.Equal(ResultatAnnulation.CommandeIntrouvable, resultat);
        Assert.Equal([messageId], await LireInbox());
    }

    // ---------- Atomicité ----------

    [Fact]
    public async Task Atomicite_si_l_inbox_echoue_la_commande_n_est_pas_annulee()
    {
        var id = await CommandeConfirmee();
        // La base REFUSE l'écriture dans l'Inbox.
        await Sql("ALTER TABLE MessagesTraites ADD CONSTRAINT CK_RefuseTout CHECK (1 = 0)");

        await Assert.ThrowsAsync<DbUpdateException>(() => Recevoir(Guid.NewGuid(), id));

        // Sans transaction commune, la commande serait annulée sans trace : un rejeu l'annulerait « une seconde fois »,
        // ou pire, une annulation partielle serait visible. Ici l'annulation est entièrement défaite.
        Assert.Equal(("Confirmee", null), await Etat(id));
        Assert.Empty(await LireInbox());
    }
}
