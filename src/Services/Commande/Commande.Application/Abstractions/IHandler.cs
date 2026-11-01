namespace Commande.Application.Abstractions;

// Contrat commun à tous les cas d'usage : permet de les envelopper avec des décorateurs (logs, validation...).
public interface IHandler<in TRequete, TReponse>
{
    Task<TReponse> Handle(TRequete requete, CancellationToken ct);
}
