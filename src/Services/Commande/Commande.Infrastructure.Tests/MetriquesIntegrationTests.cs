using System.Diagnostics.Metrics;
using Commande.Application.Abstractions;
using Commande.Application.Commandes;
using Commande.Application.Commandes.Ecriture;
using Commande.Infrastructure.Outbox;
using Commande.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Commande.Infrastructure.Tests;

// Les mesures d'ensemble : combien de messages partent, combien attendent, combien d'échecs.
// Un MeterListener joue le rôle d'OpenTelemetry (ou de dotnet-counters) : il lit les valeurs que le code produit.
public class MetriquesIntegrationTests : BaseIntegration, IDisposable
{
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

    private Task ConfirmerUneCommande() => DansUnScope(async sp =>
    {
        var cree = await sp.GetRequiredService<IHandler<CreerCommande, CommandeDto>>().Handle(
            new CreerCommande(Guid.NewGuid(), [new LigneACreer(Guid.NewGuid(), "Clavier", 50m, 2)]), CancellationToken.None);
        await sp.GetRequiredService<IHandler<ChangerStatutCommande, CommandeDto>>().Handle(
            new ChangerStatutCommande(cree.Id, ActionCommande.Confirmer), CancellationToken.None);
    });

    private Task<int> MessagesEnAttente() => DansUnScope(sp =>
        sp.GetRequiredService<CommandeDbContext>().MessagesOutbox.CountAsync(m => m.TraiteLe == null));

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
        await ConfirmerUneCommande();

        await AvecPublicateurAsync(async () => await MessagesEnAttente() == 0);

        Assert.Equal(1, Somme("commande.outbox.messages_publies", "type=CommandeConfirmeeIntegrationEvent"));
        Assert.Equal(0, Somme("commande.outbox.echecs_publication"));
        Assert.Equal(0, ValeurDeLaJauge("commande.outbox.en_attente"));
    }

    [Fact]
    public async Task Pendant_une_panne_la_jauge_montre_le_message_bloque_et_les_echecs_sont_comptes()
    {
        await ConfirmerUneCommande();
        Publicateur.EnPanne = true;
        double enAttentePendantLaPanne = -1, echecsPendantLaPanne = -1, publiesPendantLaPanne = -1;

        await AvecPublicateurAsync(
            condition: async () => await MessagesEnAttente() == 0,
            pendant: async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5)); // plusieurs cycles du publicateur passent, tous en échec
                enAttentePendantLaPanne = ValeurDeLaJauge("commande.outbox.en_attente");
                echecsPendantLaPanne = Somme("commande.outbox.echecs_publication");
                publiesPendantLaPanne = Somme("commande.outbox.messages_publies");
                Publicateur.EnPanne = false; // RabbitMQ revient
            });

        // Pendant la panne : le message attend (jauge à 1), des échecs s'accumulent, rien n'est publié.
        Assert.Equal(1, enAttentePendantLaPanne);
        Assert.True(echecsPendantLaPanne >= 1, $"échecs observés : {echecsPendantLaPanne}");
        Assert.Equal(0, publiesPendantLaPanne);

        // Au retour : le message part, la jauge retombe à zéro.
        Assert.Equal(1, Somme("commande.outbox.messages_publies"));
        Assert.Equal(0, ValeurDeLaJauge("commande.outbox.en_attente"));
    }

    [Fact]
    public void Un_message_recu_enregistre_le_compteur_et_la_duree_avec_son_resultat()
    {
        Telemetrie.MessageRecu("Annulee", 12.5);
        Telemetrie.MessageRecu("rejete", 3);

        Assert.Equal(1, Somme("commande.messages_recus", "resultat=Annulee"));
        Assert.Equal(1, Somme("commande.messages_recus", "resultat=rejete"));
        Assert.Equal(12.5, Somme("commande.message.duree_traitement", "resultat=Annulee"));
    }
}
