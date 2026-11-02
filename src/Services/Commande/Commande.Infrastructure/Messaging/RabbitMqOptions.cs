namespace Commande.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    public string Hote { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Utilisateur { get; set; } = "guest";
    public string MotDePasse { get; set; } = "guest";
    public string Exchange { get; set; } = "commande.events";
}
