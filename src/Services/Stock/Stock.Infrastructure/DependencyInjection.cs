using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stock.Application.Abstractions;
using Stock.Infrastructure.Messaging;
using Stock.Infrastructure.Outbox;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("StockDb")
            ?? throw new InvalidOperationException("La chaîne de connexion 'StockDb' est manquante.");

        services.AddDbContext<StockDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IMessagesTraites, MessagesTraites>();

        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.Section));
        services.AddHostedService<ConsommateurCommandeConfirmee>();

        // Outbox : IOutbox écrit dans la base (scoped, même DbContext) ; le publicateur lit et envoie (arrière-plan).
        services.AddSingleton<IPublicateurMessages, RabbitMqPublicateur>();
        services.AddScoped<IOutbox, OutboxEcriture>();
        services.AddHostedService<PublicateurOutbox>();
        return services;
    }
}
