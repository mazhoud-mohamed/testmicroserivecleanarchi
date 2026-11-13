using System.Diagnostics;
using Commande.Api;
using Commande.Application.Abstractions;
using Commande.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Commande.Api.Tests;

// OpenTelemetry n'enregistre jamais le corps d'une réponse. Pour qu'un 400 ou un 422 soit compréhensible dans Jaeger,
// le gestionnaire d'exceptions dépose lui-même le motif et les erreurs de champ sur le span de la requête.
public class GestionnaireExceptionsTests : IDisposable
{
    // Sans écouteur, StartActivity renvoie null et aucun span n'existe : on simule ce que fait OpenTelemetry.
    private readonly ActivityListener _ecouteur = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
    };

    private readonly ActivitySource _source = new("Tests.Api");

    public GestionnaireExceptionsTests()
    {
        ActivitySource.AddActivityListener(_ecouteur);
    }

    public void Dispose()
    {
        _ecouteur.Dispose();
        _source.Dispose();
    }

    // Faux service de réponses : on ne s'intéresse pas à l'écriture du corps, seulement au span.
    private sealed class FauxProblemDetails : Microsoft.AspNetCore.Http.IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;
        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context) => ValueTask.FromResult(true);
    }

    private static GestionnaireExceptions Creer() =>
        new(new FauxProblemDetails(), NullLogger<GestionnaireExceptions>.Instance);

    private static string? Tag(Activity activite, string cle) => activite.GetTagItem(cle)?.ToString();

    [Fact]
    public async Task Une_validation_refusee_depose_le_motif_et_les_erreurs_par_champ_sur_le_span()
    {
        using var requete = _source.StartActivity("POST api/commandes")!;
        var erreurs = new Dictionary<string, string[]>
        {
            ["ClientId"] = ["Le client est obligatoire."],
            ["Lignes[0].Quantite"] = ["La quantité doit être supérieure à zéro."]
        };
        var contexte = new DefaultHttpContext();

        var gere = await Creer().TryHandleAsync(contexte, new ValidationRequeteException(erreurs), CancellationToken.None);

        Assert.True(gere);
        Assert.Equal(400, contexte.Response.StatusCode);
        Assert.Equal(nameof(ValidationRequeteException), Tag(requete, "erreur.type"));
        Assert.Contains("validation", Tag(requete, "erreur.motif")!);
        var details = Tag(requete, "erreur.details")!;
        Assert.Contains("Le client est obligatoire.", details);
        Assert.Contains("Lignes[0].Quantite", details);
    }

    [Fact]
    public async Task Une_regle_metier_violee_depose_son_motif_sur_le_span()
    {
        using var requete = _source.StartActivity("POST api/commandes/{id}/expedier")!;
        var contexte = new DefaultHttpContext();

        await Creer().TryHandleAsync(
            contexte, new DomainException("Seule une commande confirmée peut être expédiée."), CancellationToken.None);

        Assert.Equal(422, contexte.Response.StatusCode);
        Assert.Equal(nameof(DomainException), Tag(requete, "erreur.type"));
        Assert.Equal("Seule une commande confirmée peut être expédiée.", Tag(requete, "erreur.motif"));
        Assert.Null(requete.GetTagItem("erreur.details")); // pas de détail par champ pour une règle métier
    }

    [Fact]
    public async Task Une_commande_introuvable_depose_son_motif_sur_le_span()
    {
        using var requete = _source.StartActivity("GET api/commandes/{id}")!;
        var contexte = new DefaultHttpContext();

        await Creer().TryHandleAsync(contexte, new NonTrouveException("Commande", Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(404, contexte.Response.StatusCode);
        Assert.Equal(nameof(NonTrouveException), Tag(requete, "erreur.type"));
    }

    [Fact]
    public async Task Une_exception_inattendue_n_est_pas_geree_et_ne_depose_rien()
    {
        using var requete = _source.StartActivity("GET api/commandes")!;
        var contexte = new DefaultHttpContext();

        var gere = await Creer().TryHandleAsync(contexte, new InvalidOperationException("boom"), CancellationToken.None);

        // Laissée à ASP.NET (500) : on ne copie pas dans la trace le message d'une exception qu'on ne connaît pas.
        Assert.False(gere);
        Assert.Null(requete.GetTagItem("erreur.motif"));
    }

    [Fact]
    public async Task Sans_trace_en_cours_la_reponse_est_quand_meme_produite()
    {
        Activity.Current = null;
        var contexte = new DefaultHttpContext();

        var gere = await Creer().TryHandleAsync(contexte, new DomainException("règle"), CancellationToken.None);

        Assert.True(gere);
        Assert.Equal(422, contexte.Response.StatusCode);
    }
}
