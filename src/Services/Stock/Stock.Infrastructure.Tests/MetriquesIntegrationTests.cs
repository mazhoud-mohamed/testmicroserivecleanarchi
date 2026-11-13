using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stock.Application.Abstractions;
using Stock.Application.Articles;
using Stock.Domain.Articles;
using Stock.Infrastructure.Outbox;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure.Tests;

// Les mesures d'ensemble : combien de messages partent, combien attendent, combien d'échecs.
// Un MeterListener joue le rôle d'OpenTelemetry (ou de dotnet-counters) : il lit les valeurs que le code produit.
public class MetriquesIntegrationTests : BaseIntegration, IDisposable
{
    private static readonly Guid Clavier = Guid.NewGuid();

    private readonly MeterListener _ecouteur = new();
    private readonly List<(string Nom, double Valeur, string Etiquettes)> _mesures = [];
    private readonly object _verrou = new();

    public MetriquesIntegrationTests()
    {
        Telemetrie.DefinirEnAttente(0); // l'état d'un test précédent ne doit pas fausser celui-ci

        _ecouteur.InstrumentPublished = (instrument, ecouteur) =>
        {
            if (instrument.Meter.Name == Telemetrie.NomSource) ecouteur.EnableMeasurementEvents(instrument);
        };
        _ecouteur.SetMeasurementEventCallback<long>((i, valeur, etiquettes, _) => Noter(i.Name, valeur, etiquettes));
        _ecouteur.SetMeasurementEventCallback<int>((i, valeur, etiquettes, _) => Noter(i.Name, valeur, etiquettes));
        _ecouteur.SetMeasurementEventCallback<double>((i, valeur, etiquettes, _) => Noter(i.Name, valeur, etiquettes));
        _ecouteur.Start();
    }

    private void Noter(string nom, double valeur, ReadOnlySpan<KeyValuePair<string, object?>> etiquettes)
    {
        var texte = string.Join(",", etiquettes.ToArray().Select(e => $"{e.Key}={e.Value}"));
        lock (_verrou) _mesures.Add((nom, valeur, texte));
    }

    public void Dispose() => _ecouteur.Dispose();

    // Somme d'un compteur (éventuellement pour une étiquette donnée).
    private double Somme(string nom, string? etiquette = null)
    {
        lock (_verrou)
            return _mesures.Where(m => m.Nom == nom && (etiquette is null || m.Etiquettes.Contains(etiquette)))
                .Sum(m => m.Valeur);
    }

    // Valeur actuelle de la jauge : on demande aux instruments « observables » de se mesurer maintenant.
    private double ValeurDeLaJauge(string nom)
    {
        lock (_verrou) _mesures.RemoveAll(m => m.Nom == nom);
        _ecouteur.RecordObservableInstruments();
        lock (_verrou) return _mesures.Last(m => m.Nom == nom).Valeur;
    }

    // Un refus de réservation écrit un message ReservationEchouee dans l'Outbox de Stock.
    private Task ProduireUnRefus() => DansUnScope(async sp =>
    {
        var depot = sp.GetRequiredService<IStockRepository>();
        await depot.AjouterAsync(new ArticleStock(Clavier, 1), CancellationToken.None);
        await depot.SauvegarderAsync(CancellationToken.None);
    }).ContinueWith(_ => DansUnScope(sp => sp.GetRequiredService<ReserverStockPourCommandeHandler>().Handle(
        new ReserverStockPourCommande(Guid.NewGuid(), Guid.NewGuid(), [new LigneAReserver(Clavier, 9)]),
        CancellationToken.None))).Unwrap();

    private Task<int> MessagesEnAttente() => DansUnScope(sp =>
        sp.GetRequiredService<StockDbContext>().MessagesOutbox.CountAsync(m => m.TraiteLe == null));

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
    public async Task Un_message_publie_incremente_le_compteur_et_la_jauge_retombe_a_zero()
    {
        await ProduireUnRefus();

        await AvecPublicateurAsync(async () => await MessagesEnAttente() == 0);

        Assert.Equal(1, Somme("stock.outbox.messages_publies", "type=ReservationEchoueeIntegrationEvent"));
        Assert.Equal(0, Somme("stock.outbox.echecs_publication"));
        Assert.Equal(0, ValeurDeLaJauge("stock.outbox.en_attente"));
    }

    [Fact]
    public async Task Pendant_une_panne_la_jauge_montre_le_message_bloque_et_les_echecs_sont_comptes()
    {
        await ProduireUnRefus();
        Publicateur.EnPanne = true;
        double enAttentePendantLaPanne = -1, echecsPendantLaPanne = -1, publiesPendantLaPanne = -1;

        await AvecPublicateurAsync(
            condition: async () => await MessagesEnAttente() == 0,
            pendant: async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5)); // plusieurs cycles du publicateur passent, tous en échec
                enAttentePendantLaPanne = ValeurDeLaJauge("stock.outbox.en_attente");
                echecsPendantLaPanne = Somme("stock.outbox.echecs_publication");
                publiesPendantLaPanne = Somme("stock.outbox.messages_publies");
                Publicateur.EnPanne = false; // RabbitMQ revient
            });

        // Pendant la panne : le message attend (jauge à 1), des échecs s'accumulent, rien n'est publié.
        Assert.Equal(1, enAttentePendantLaPanne);
        Assert.True(echecsPendantLaPanne >= 1, $"échecs observés : {echecsPendantLaPanne}");
        Assert.Equal(0, publiesPendantLaPanne);

        // Au retour : le message part, la jauge retombe à zéro.
        Assert.Equal(1, Somme("stock.outbox.messages_publies"));
        Assert.Equal(0, ValeurDeLaJauge("stock.outbox.en_attente"));
    }

    [Fact]
    public void Un_message_recu_enregistre_le_compteur_et_la_duree_avec_son_resultat()
    {
        Telemetrie.MessageRecu("Refusee", 8.25);
        Telemetrie.MessageRecu("conflit_concurrence", 1);

        Assert.Equal(1, Somme("stock.messages_recus", "resultat=Refusee"));
        Assert.Equal(1, Somme("stock.messages_recus", "resultat=conflit_concurrence"));
        Assert.Equal(8.25, Somme("stock.message.duree_traitement", "resultat=Refusee"));
    }
}
