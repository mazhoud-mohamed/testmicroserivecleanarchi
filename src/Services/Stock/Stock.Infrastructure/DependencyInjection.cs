using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stock.Application.Abstractions;
using Stock.Infrastructure.Messaging;
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

        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.Section));
        services.AddHostedService<ConsommateurCommandeConfirmee>();
        return services;
    }
}
