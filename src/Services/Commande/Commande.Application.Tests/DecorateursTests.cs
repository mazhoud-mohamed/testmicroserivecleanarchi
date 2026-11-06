using Commande.Application.Abstractions;
using Commande.Application.Commandes.Ecriture;
using Commande.Application.Decorateurs;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Commande.Application.Tests;

// Un handler factice qui compte ses appels et renvoie / lève ce qu'on lui demande.
public class HandlerEspion<TRequete, TReponse>(Func<TRequete, TReponse> comportement) : IHandler<TRequete, TReponse>
{
    public int NombreAppels { get; private set; }

    public Task<TReponse> Handle(TRequete requete, CancellationToken ct)
    {
        NombreAppels++;
        return Task.FromResult(comportement(requete));
    }
}

public class HandlerAvecValidationTests
{
    private static CreerCommande Valide() => new(Guid.NewGuid(), [new LigneACreer(Guid.NewGuid(), "Clavier", 10m, 1)]);

    private static CreerCommande Invalide() => new(Guid.Empty, []);

    private static HandlerEspion<CreerCommande, string> Espion() => new(_ => "ok");

    [Fact]
    public async Task Requete_valide_est_transmise_au_handler()
    {
        var espion = Espion();
        var decorateur = new HandlerAvecValidation<CreerCommande, string>(
            espion, [new CreerCommandeValidator()]);

        var reponse = await decorateur.Handle(Valide(), CancellationToken.None);

        Assert.Equal("ok", reponse);
        Assert.Equal(1, espion.NombreAppels);
    }

    [Fact]
    public async Task Requete_invalide_leve_ValidationRequeteException_sans_appeler_le_handler()
    {
        var espion = Espion();
        var decorateur = new HandlerAvecValidation<CreerCommande, string>(
            espion, [new CreerCommandeValidator()]);

        var exception = await Assert.ThrowsAsync<ValidationRequeteException>(
            () => decorateur.Handle(Invalide(), CancellationToken.None));

        Assert.Equal(0, espion.NombreAppels);
        Assert.Contains(nameof(CreerCommande.ClientId), exception.Erreurs.Keys);
        Assert.Contains(nameof(CreerCommande.Lignes), exception.Erreurs.Keys);
    }

    [Fact]
    public async Task Les_erreurs_de_tous_les_champs_sont_remontees_ensemble()
    {
        var decorateur = new HandlerAvecValidation<CreerCommande, string>(
            Espion(), [new CreerCommandeValidator()]);
        var requete = new CreerCommande(Guid.Empty, [new LigneACreer(Guid.Empty, "", -1m, 0)]);

        var exception = await Assert.ThrowsAsync<ValidationRequeteException>(
            () => decorateur.Handle(requete, CancellationToken.None));

        // ClientId + les 4 champs de la ligne : tout est signalé d'un coup, pas une erreur à la fois.
        Assert.Equal(5, exception.Erreurs.Count);
    }

    [Fact]
    public async Task Sans_validateur_la_requete_passe_telle_quelle()
    {
        var espion = Espion();
        var decorateur = new HandlerAvecValidation<CreerCommande, string>(espion, []);

        await decorateur.Handle(Invalide(), CancellationToken.None);

        Assert.Equal(1, espion.NombreAppels);
    }
}

public class HandlerAvecLogsTests
{
    [Fact]
    public async Task Renvoie_la_reponse_du_handler()
    {
        var decorateur = new HandlerAvecLogs<string, int>(
            new HandlerEspion<string, int>(s => s.Length),
            NullLogger<HandlerAvecLogs<string, int>>.Instance);

        var reponse = await decorateur.Handle("abcd", CancellationToken.None);

        Assert.Equal(4, reponse);
    }

    [Fact]
    public async Task Les_exceptions_du_handler_remontent_sans_etre_avalees()
    {
        var decorateur = new HandlerAvecLogs<string, int>(
            new HandlerEspion<string, int>(_ => throw new InvalidOperationException("boom")),
            NullLogger<HandlerAvecLogs<string, int>>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => decorateur.Handle("x", CancellationToken.None));
    }
}
