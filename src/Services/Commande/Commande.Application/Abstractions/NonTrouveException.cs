namespace Commande.Application.Abstractions;

public class NonTrouveException(string nom, object cle) : Exception($"{nom} '{cle}' est introuvable.");
