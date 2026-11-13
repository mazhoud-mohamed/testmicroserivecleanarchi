using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace Stock.Infrastructure;

// Point central de l'observabilité côté Infrastructure.
//
// Un « span » est une étape d'une trace. Ceux qu'ASP.NET et SqlClient créent automatiquement ne suffisent pas :
// entre deux services, le contexte de trace doit voyager À LA MAIN dans le message (en-tête « traceparent »,
// norme W3C : version-traceId-spanId-drapeaux). C'est ce que font les méthodes ci-dessous.
public static class Telemetrie
{
    // Doit être déclaré dans OpenTelemetry (AddSource) : sans cela, StartActivity renvoie null et rien n'est tracé.
    public const string NomSource = "Stock.Infrastructure";

    public static readonly ActivitySource Source = new(NomSource);

    public const string EnTeteTraceParent = "traceparent";

    // ---------- MÉTRIQUES ----------
    // Une trace raconte UNE opération ; une métrique MESURE le comportement d'ensemble dans le temps : combien de messages
    // sont partis, combien attendent, combien de temps dure un traitement. Ce sont des chiffres agrégés, très peu coûteux.
    //
    // Règle d'or : les étiquettes (tags) ne doivent avoir qu'un petit nombre de valeurs possibles (un type, un résultat).
    // Jamais un identifiant ni un texte libre : chaque valeur différente crée une série de plus à stocker.
    public static readonly Meter Mesures = new(NomSource);

    // Compteurs : ils ne font que monter ; on s'intéresse à leur vitesse (messages par minute).
    private static readonly Counter<long> MessagesPublies = Mesures.CreateCounter<long>(
        "stock.outbox.messages_publies", "{message}", "Messages envoyés à RabbitMQ depuis l'Outbox.");

    private static readonly Counter<long> EchecsPublication = Mesures.CreateCounter<long>(
        "stock.outbox.echecs_publication", "{echec}", "Cycles du publicateur terminés en erreur (RabbitMQ ou base indisponible).");

    private static readonly Counter<long> MessagesRecus = Mesures.CreateCounter<long>(
        "stock.messages_recus", "{message}", "Messages reçus de RabbitMQ, selon l'issue du traitement.");

    // Histogramme : répartition des durées (moyenne, percentiles) plutôt qu'une seule valeur.
    private static readonly Histogram<double> DureeTraitement = Mesures.CreateHistogram<double>(
        "stock.message.duree_traitement", "ms", "Durée du traitement d'un message reçu.");

    // Jauge : une valeur qui monte ET descend. Ici le nombre de messages qui attendent d'être envoyés.
    // Une valeur qui ne redescend pas est le signe que RabbitMQ ou le publicateur est bloqué.
    private static int _enAttente;

    public static readonly ObservableGauge<int> JaugeEnAttente = Mesures.CreateObservableGauge(
        "stock.outbox.en_attente", () => Volatile.Read(ref _enAttente), "{message}",
        "Messages de l'Outbox pas encore envoyés.");

    public static void DefinirEnAttente(int nombre) => Volatile.Write(ref _enAttente, nombre);

    public static void MessagePublie(string type) =>
        MessagesPublies.Add(1, new KeyValuePair<string, object?>("type", type));

    public static void EchecDePublication() => EchecsPublication.Add(1);

    public static void MessageRecu(string resultat, double dureeMs)
    {
        var etiquette = new KeyValuePair<string, object?>("resultat", resultat);
        MessagesRecus.Add(1, etiquette);
        DureeTraitement.Record(dureeMs, etiquette);
    }

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
