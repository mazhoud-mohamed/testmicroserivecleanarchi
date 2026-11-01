namespace Commande.Application.Abstractions;

// Levée quand une requête est invalide ; transporte les erreurs par champ pour l'API.
public class ValidationRequeteException(IDictionary<string, string[]> erreurs)
    : Exception("Une ou plusieurs erreurs de validation sont survenues.")
{
    public IDictionary<string, string[]> Erreurs { get; } = erreurs;
}
