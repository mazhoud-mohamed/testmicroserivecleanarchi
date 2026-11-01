using Commande.Application.Abstractions;
using FluentValidation;

namespace Commande.Application.Decorateurs;

// Exécute les validateurs de la requête avant le handler. Sans validateur enregistré, la requête passe telle quelle.
public class HandlerAvecValidation<TRequete, TReponse>(
    IHandler<TRequete, TReponse> inner,
    IEnumerable<IValidator<TRequete>> validateurs) : IHandler<TRequete, TReponse>
{
    public async Task<TReponse> Handle(TRequete requete, CancellationToken ct)
    {
        var contexte = new ValidationContext<TRequete>(requete);
        var resultats = await Task.WhenAll(validateurs.Select(v => v.ValidateAsync(contexte, ct)));

        var erreurs = resultats
            .SelectMany(r => r.Errors)
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());

        if (erreurs.Count > 0)
            throw new ValidationRequeteException(erreurs);

        return await inner.Handle(requete, ct);
    }
}
