using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stock.Api;
using Stock.Domain.Common;

namespace Stock.Api.Tests;

// OpenTelemetry n'enregistre jamais le corps d'une réponse. Pour qu'un 422 ou un 409 soit compréhensible dans Jaeger,
// le gestionnaire d'exceptions dépose lui-même le motif sur le span de la requête.
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

    // Faux service de réponses : on ne s'intéresse pas à l'écriture du corps, seulement au code et au span.
    private sealed class FauxProblemDetails : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;
        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context) => ValueTask.FromResult(true);
    }

    private static GestionnaireExceptions Creer() =>
        new(new FauxProblemDetails(), NullLogger<GestionnaireExceptions>.Instance);

    private static string? Tag(Activity activite, string cle) => activite.GetTagItem(cle)?.ToString();

    [Fact]
    public async Task Une_regle_metier_violee_donne_un_422_et_depose_son_motif_sur_le_span()
    {
        using var requete = _source.StartActivity("POST api/stocks/approvisionner")!;
        var contexte = new DefaultHttpContext();

        var gere = await Creer().TryHandleAsync(
            contexte, new DomainException("La quantité à ajouter doit être supérieure à zéro."), CancellationToken.None);

        Assert.True(gere);
        Assert.Equal(422, contexte.Response.StatusCode);
        Assert.Equal(nameof(DomainException), Tag(requete, "erreur.type"));
        Assert.Equal("La quantité à ajouter doit être supérieure à zéro.", Tag(requete, "erreur.motif"));
    }

    [Fact]
    public async Task Un_conflit_de_concurrence_donne_un_409_et_non_un_500()
    {
        using var requete = _source.StartActivity("POST api/stocks/approvisionner")!;
        var contexte = new DefaultHttpContext();

        var gere = await Creer().TryHandleAsync(
            contexte, new DbUpdateConcurrencyException("la ligne a changé"), CancellationToken.None);

        Assert.True(gere);
        Assert.Equal(409, contexte.Response.StatusCode);
        Assert.Equal(nameof(DbUpdateConcurrencyException), Tag(requete, "erreur.type"));
        Assert.Contains("Réessayez", Tag(requete, "erreur.motif")!);
    }

    [Fact]
    public async Task Une_exception_inattendue_n_est_pas_geree_et_ne_depose_rien()
    {
        using var requete = _source.StartActivity("GET api/stocks")!;
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
