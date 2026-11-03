namespace Stock.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    public string Hote { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Utilisateur { get; set; } = "guest";
    public string MotDePasse { get; set; } = "guest";

    // Exchange sur lequel Commande publie : Stock doit utiliser exactement le même nom.
    public string Exchange { get; set; } = "commande.events";

    // Boîte aux lettres PROPRE à Stock : c'est lui qui la déclare et qui y lit ses messages.
    public string File { get; set; } = "stock.commande";
}
