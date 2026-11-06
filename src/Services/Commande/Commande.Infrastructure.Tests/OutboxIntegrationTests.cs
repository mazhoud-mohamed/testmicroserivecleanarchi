using Commande.Application.Abstractions;
using Commande.Application.Commandes;
using Commande.Application.Commandes.Ecriture;
using Commande.Domain.Common;
using Commande.Infrastructure.Outbox;
using Commande.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Infrastructure.Tests;

// Tests d'intégration sur une vraie base SQL Server : ils prouvent ce que les tests unitaires ne peuvent pas prouver,
// à savoir que la commande et son message Outbox sont écrits dans la MÊME transaction.
public class OutboxIntegrationTests : BaseIntegration
{
    private Task<CommandeDto> Creer() => DansUnScope(sp =>
        sp.GetRequiredService<IHandler<CreerCommande, CommandeDto>>().Handle(
            new CreerCommande(Guid.NewGuid(), [new LigneACreer(Guid.NewGuid(), "Clavier", 50m, 2)]),
            CancellationToken.None));

    private Task<CommandeDto> Changer(Guid id, ActionCommande action) => DansUnScope(sp =>
        sp.GetRequiredService<IHandler<ChangerStatutCommande, CommandeDto>>().Handle(
            new ChangerStatutCommande(id, action), CancellationToken.None));

    private Task<List<MessageOutbox>> LireOutbox() => DansUnScope(sp =>
        sp.GetRequiredService<CommandeDbContext>().MessagesOutbox.AsNoTracking().OrderBy(m => m.CreeLe).ToListAsync());

    private Task<string> LireStatut(Guid id) => DansUnScope(async sp =>
        (await sp.GetRequiredService<CommandeDbContext>().Commandes.AsNoTracking().SingleAsync(c => c.Id == id))
        .Statut.ToString());

    // ---------- ÉCRITURE : la commande et le message partent ensemble ----------

    [Fact]
    public async Task Confirmer_ecrit_le_statut_et_le_message_outbox_ensemble()
    {
        var commande = await Creer();

        await Changer(commande.Id, ActionCommande.Confirmer);

        Assert.Equal("Confirmee", await LireStatut(commande.Id));
        var message = Assert.Single(await LireOutbox());
        Assert.Equal("commande.confirmee", message.CleRoutage);
        Assert.Equal("CommandeConfirmeeIntegrationEvent", message.Type);
        Assert.Null(message.TraiteLe);                       // pas encore envoyé
        Assert.Contains(commande.Id.ToString(), message.Contenu);
    }

    [Fact]
    public async Task Creer_une_commande_n_ecrit_aucun_message()
    {
        await Creer();

        Assert.Empty(await LireOutbox());
    }

    [Fact]
    public async Task Confirmer_deux_fois_n_ecrit_qu_un_seul_message()
    {
        var commande = await Creer();
        await Changer(commande.Id, ActionCommande.Confirmer);

        await Assert.ThrowsAsync<DomainException>(() => Changer(commande.Id, ActionCommande.Confirmer));

        Assert.Single(await LireOutbox());
    }

    [Fact]
    public async Task Echec_metier_n_ecrit_aucun_message()
    {
        // Une commande SANS ligne ne peut pas être confirmée : le Domain refuse avant tout événement.
        Guid id = default;
        await DansUnScope(async sp =>
        {
            var depot = sp.GetRequiredService<ICommandeRepository>();
            var vide = new CommandeAgregat(Guid.NewGuid());
            id = vide.Id;
            await depot.AjouterAsync(vide, CancellationToken.None);
            await depot.SauvegarderAsync(CancellationToken.None);
        });

        await Assert.ThrowsAsync<DomainException>(() => Changer(id, ActionCommande.Confirmer));

        Assert.Empty(await LireOutbox());
        Assert.Equal("EnAttente", await LireStatut(id));
    }

    [Fact]
    public async Task Atomicite_si_l_ecriture_du_message_echoue_le_statut_n_est_pas_modifie()
    {
        var commande = await Creer();

        // On force la base à REFUSER l'insertion du message : la commande, elle, serait modifiable.
        // Si les deux écritures n'étaient pas dans la même transaction, la commande passerait quand même à « Confirmee ».
        await DansUnScope(sp => sp.GetRequiredService<CommandeDbContext>().Database.ExecuteSqlRawAsync(
            "ALTER TABLE MessagesOutbox ADD CONSTRAINT CK_RefuseTout CHECK (CleRoutage <> 'commande.confirmee')"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Changer(commande.Id, ActionCommande.Confirmer));

        Assert.Equal("EnAttente", await LireStatut(commande.Id));
        Assert.Empty(await LireOutbox());
    }

    // ---------- PUBLICATION : le service d'arrière-plan envoie puis marque ----------

    // Démarre le vrai PublicateurOutbox (avec le faux RabbitMQ) et attend qu'une condition soit remplie.
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
    public async Task Le_publicateur_envoie_le_message_puis_le_marque_traite()
    {
        var commande = await Creer();
        await Changer(commande.Id, ActionCommande.Confirmer);

        await AvecPublicateurAsync(async () => (await LireOutbox()).All(m => m.TraiteLe is not null));

        var envoye = Assert.Single(Publicateur.Publies);
        Assert.Equal("commande.confirmee", envoye.Cle);
        Assert.Contains(commande.Id.ToString(), envoye.Contenu);
        var ligne = Assert.Single(await LireOutbox());
        Assert.Equal(ligne.Id, envoye.Id);
    }

    [Fact]
    public async Task Si_rabbitmq_est_en_panne_le_message_reste_en_attente_puis_part_au_retour()
    {
        var commande = await Creer();
        await Changer(commande.Id, ActionCommande.Confirmer);
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

    [Fact]
    public async Task Le_publicateur_n_envoie_pas_deux_fois_un_message_deja_traite()
    {
        var commande = await Creer();
        await Changer(commande.Id, ActionCommande.Confirmer);
        await AvecPublicateurAsync(async () => (await LireOutbox()).All(m => m.TraiteLe is not null));

        // Nouveau démarrage du publicateur : le message est déjà marqué, il ne doit pas repartir.
        await AvecPublicateurAsync(
            condition: () => Task.FromResult(true),
            pendant: () => Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Single(Publicateur.Publies);
    }
}
