namespace Commande.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    public string Hote { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Utilisateur { get; set; } = "guest";
    public string MotDePasse { get; set; } = "guest";
    // Exchange sur lequel Commande PUBLIE ses événements.
    public string Exchange { get; set; } = "commande.events";

    // Exchange sur lequel Stock publie : Commande doit utiliser exactement le même nom pour s'y abonner.
    public string ExchangeStock { get; set; } = "stock.events";

    // Boîte aux lettres PROPRE à Commande pour les messages de Stock : c'est lui qui la déclare et qui y lit.
    public string FileStock { get; set; } = "commande.stock";
}
