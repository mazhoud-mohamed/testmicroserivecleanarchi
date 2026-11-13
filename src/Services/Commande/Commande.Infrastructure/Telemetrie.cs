using System.Diagnostics;
using System.Text;

namespace Commande.Infrastructure;

// Point central de l'observabilité côté Infrastructure.
//
// Un « span » est une étape d'une trace. Ceux qu'ASP.NET et SqlClient créent automatiquement ne suffisent pas :
// entre deux services, le contexte de trace doit voyager À LA MAIN dans le message (en-tête « traceparent »,
// norme W3C : version-traceId-spanId-drapeaux). C'est ce que font les méthodes ci-dessous.
public static class Telemetrie
{
    // Doit être déclaré dans OpenTelemetry (AddSource) : sans cela, StartActivity renvoie null et rien n'est tracé.
    public const string NomSource = "Commande.Infrastructure";

    public static readonly ActivitySource Source = new(NomSource);

    public const string EnTeteTraceParent = "traceparent";

    // Reconstruit un contexte de trace à partir d'un « traceparent » ; un contexte vide (default) si absent ou invalide,
    // auquel cas le span démarré devient le début d'une nouvelle trace.
    public static ActivityContext ContexteDepuis(string? traceParent) =>
        ActivityContext.TryParse(traceParent, null, out var contexte) ? contexte : default;

    // RabbitMQ rend les en-têtes texte sous forme d'octets.
    public static string? LireTraceParent(IDictionary<string, object?>? entetes) =>
        entetes is not null && entetes.TryGetValue(EnTeteTraceParent, out var valeur)
            ? valeur switch { byte[] octets => Encoding.UTF8.GetString(octets), string texte => texte, _ => null }
            : null;
}
