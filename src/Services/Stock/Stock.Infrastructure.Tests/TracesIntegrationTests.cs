using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stock.Application.Abstractions;
using Stock.Application.Articles;
using Stock.Domain.Articles;
using Stock.Infrastructure.Outbox;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure.Tests;

// La trace du traitement d'un message doit SURVIVRE à l'Outbox : le message de compensation est écrit pendant le
// traitement, mais envoyé plus tard par un service d'arrière-plan qui n'a aucun contexte.
// Sans propagation, la trace serait coupée en deux au milieu de la saga.
public class TracesIntegrationTests : BaseIntegration, IDisposable
{
    // Sans écouteur, StartActivity renvoie null et aucun span n'existe : on simule ce que fait OpenTelemetry.
    private readonly ActivityListener _ecouteur = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
    };

    private readonly ActivitySource _source = new("Tests.Consommateur");

    public TracesIntegrationTests()
    {
        ActivitySource.AddActivityListener(_ecouteur);
    }

    private static readonly Guid Clavier = Guid.NewGuid();

    private Task Approvisionner(int quantite) => DansUnScope(async sp =>
    {
        var depot = sp.GetRequiredService<IStockRepository>();
        await depot.AjouterAsync(new ArticleStock(Clavier, quantite), CancellationToken.None);
        await depot.SauvegarderAsync(CancellationToken.None);
    });

    private Task<ResultatReservation> Recevoir(int quantite) => DansUnScope(sp =>
        sp.GetRequiredService<ReserverStockPourCommandeHandler>().Handle(
            new ReserverStockPourCommande(Guid.NewGuid(), Guid.NewGuid(), [new LigneAReserver(Clavier, quantite)]),
            CancellationToken.None));

    private Task<List<MessageOutbox>> LireOutbox() => DansUnScope(sp =>
        sp.GetRequiredService<StockDbContext>().MessagesOutbox.AsNoTracking().ToListAsync());

    private async Task AttendrePublicationAsync()
    {
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
    }

    [Fact]
    public async Task L_outbox_conserve_le_contexte_de_trace_du_traitement_qui_a_ecrit_le_refus()
    {
        await Approvisionner(1);
        using var traitement = _source.StartActivity("traitement simulé")!;

        var resultat = await Recevoir(9);

        Assert.Equal(ResultatReservation.Refusee, resultat);
        Assert.Equal(traitement.Id, Assert.Single(await LireOutbox()).TraceParent);
    }

    [Fact]
    public async Task Sans_trace_en_cours_le_message_est_ecrit_sans_contexte()
    {
        await Approvisionner(1);
        Activity.Current = null;

        await Recevoir(9);

        Assert.Null(Assert.Single(await LireOutbox()).TraceParent);
    }

    [Fact]
    public async Task Le_publicateur_restaure_la_trace_d_origine_au_moment_de_publier()
    {
        await Approvisionner(1);
        string traceOrigine;
        using (var traitement = _source.StartActivity("traitement simulé")!)
        {
            traceOrigine = traitement.TraceId.ToString();
            await Recevoir(9);
        }

        // Le traitement est TERMINÉ : le publicateur n'hérite d'aucun contexte ambiant. Il ne peut retrouver la trace
        // qu'à partir de la colonne TraceParent de l'Outbox.
        Activity.Current = null;
        await AttendrePublicationAsync();

        // Le message de compensation est publié DANS la même trace que le traitement d'origine.
        Assert.Equal(traceOrigine, Assert.Single(Publicateur.TraceIds));
    }

    public void Dispose()
    {
        _ecouteur.Dispose();
        _source.Dispose();
    }
}
