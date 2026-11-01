using Commande.Application.Abstractions;
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
        return services;
    }
}
