using System.Diagnostics;
using Commande.Application.Abstractions;
using Commande.Application.Commandes;
using Commande.Application.Commandes.Ecriture;
using Commande.Infrastructure.Outbox;
using Commande.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Commande.Infrastructure.Tests;

// La trace d'une requête doit SURVIVRE à l'Outbox : le message est écrit pendant la requête, mais envoyé plus tard
// par un service d'arrière-plan qui n'a aucun contexte. Sans propagation, la trace serait coupée en deux.
public class TracesIntegrationTests : BaseIntegration, IDisposable
{
    // Sans écouteur, StartActivity renvoie null et aucun span n'existe : on simule ce que fait OpenTelemetry.
    private readonly ActivityListener _ecouteur = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
    };

    private readonly ActivitySource _source = new("Tests.Requete");

    public TracesIntegrationTests()
    {
        ActivitySource.AddActivityListener(_ecouteur);
    }

    private Task<CommandeDto> Creer() => DansUnScope(sp =>
        sp.GetRequiredService<IHandler<CreerCommande, CommandeDto>>().Handle(
            new CreerCommande(Guid.NewGuid(), [new LigneACreer(Guid.NewGuid(), "Clavier", 50m, 2)]),
            CancellationToken.None));

    private Task<CommandeDto> Changer(Guid id, ActionCommande action) => DansUnScope(sp =>
        sp.GetRequiredService<IHandler<ChangerStatutCommande, CommandeDto>>().Handle(
            new ChangerStatutCommande(id, action), CancellationToken.None));

    private Task<List<MessageOutbox>> LireOutbox() => DansUnScope(sp =>
        sp.GetRequiredService<CommandeDbContext>().MessagesOutbox.AsNoTracking().ToListAsync());

    [Fact]
    public async Task L_outbox_conserve_le_contexte_de_trace_de_la_requete_qui_a_ecrit_le_message()
    {
        using var requete = _source.StartActivity("requête simulée")!;
        var commande = await Creer();

        await Changer(commande.Id, ActionCommande.Confirmer);

        Assert.Equal(requete.Id, Assert.Single(await LireOutbox()).TraceParent);
    }

    [Fact]
    public async Task Sans_trace_en_cours_le_message_est_ecrit_sans_contexte()
    {
        Activity.Current = null;
        var commande = await Creer();

        await Changer(commande.Id, ActionCommande.Confirmer);

        Assert.Null(Assert.Single(await LireOutbox()).TraceParent);
    }

    [Fact]
    public async Task Le_publicateur_restaure_la_trace_d_origine_au_moment_de_publier()
    {
        string traceOrigine;
        using (var requete = _source.StartActivity("requête simulée")!)
        {
            traceOrigine = requete.TraceId.ToString();
            var commande = await Creer();
            await Changer(commande.Id, ActionCommande.Confirmer);
        }

        // La requête est TERMINÉE : le publicateur n'hérite d'aucun contexte ambiant. Il ne peut retrouver la trace
        // qu'à partir de la colonne TraceParent de l'Outbox.
        Activity.Current = null;
        var publicateur = Services.GetServices<IHostedService>().OfType<PublicateurOutbox>().Single();
        await publicateur.StartAsync(CancellationToken.None);
        try
        {
            var limite = DateTime.UtcNow.AddSeconds(15);
            while (Publicateur.TraceIds.Count == 0)
            {
                Assert.True(DateTime.UtcNow < limite, "Aucune publication en 15 secondes.");
                await Task.Delay(200);
            }
        }
        finally
        {
            await publicateur.StopAsync(CancellationToken.None);
        }

        // Le message est publié DANS la même trace que la requête d'origine.
        Assert.Equal(traceOrigine, Assert.Single(Publicateur.TraceIds));
    }

    [Fact]
    public async Task Sans_contexte_d_origine_le_publicateur_demarre_une_nouvelle_trace_sans_echouer()
    {
        Activity.Current = null;
        var commande = await Creer();
        await Changer(commande.Id, ActionCommande.Confirmer);

        Activity.Current = null;
        var publicateur = Services.GetServices<IHostedService>().OfType<PublicateurOutbox>().Single();
        await publicateur.StartAsync(CancellationToken.None);
        try
        {
            var limite = DateTime.UtcNow.AddSeconds(15);
            while (Publicateur.TraceIds.Count == 0)
            {
                Assert.True(DateTime.UtcNow < limite, "Aucune publication en 15 secondes.");
                await Task.Delay(200);
            }
        }
        finally
        {
            await publicateur.StopAsync(CancellationToken.None);
        }

        // Une trace existe quand même (celle du span producteur), elle n'est simplement rattachée à aucune requête.
        Assert.NotNull(Assert.Single(Publicateur.TraceIds));
    }

    public void Dispose()
    {
        _ecouteur.Dispose();
        _source.Dispose();
    }
}
