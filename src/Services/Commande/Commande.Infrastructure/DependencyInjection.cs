using Commande.Application.Abstractions;
using Commande.Infrastructure.Messaging;
using Commande.Infrastructure.Outbox;
using Commande.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Commande.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("CommandeDb")
            ?? throw new InvalidOperationException("La chaîne de connexion 'CommandeDb' est manquante.");

        services.AddDbContext<CommandeDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<ICommandeRepository, CommandeRepository>();

        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.Section));
        services.AddSingleton<IPublicateurMessages, RabbitMqPublicateur>();

        // Outbox : IOutbox écrit dans la base (scoped, même DbContext) ; le publicateur lit et envoie (arrière-plan).
        services.AddScoped<IOutbox, OutboxEcriture>();
        services.AddHostedService<PublicateurOutbox>();

        // Inbox + consommateur du chemin retour de la saga (messages publiés par Stock).
        services.AddScoped<IMessagesTraites, MessagesTraites>();
        services.AddHostedService<ConsommateurReservationEchouee>();
        return services;
    }
}
