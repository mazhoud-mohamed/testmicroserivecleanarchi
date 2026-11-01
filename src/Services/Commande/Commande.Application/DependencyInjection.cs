using Commande.Application.Commandes.Ecriture;
using Commande.Application.Commandes.Lecture;
using Microsoft.Extensions.DependencyInjection;

namespace Commande.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreerCommandeHandler>();
        services.AddScoped<ChangerStatutCommandeHandler>();
        services.AddScoped<ObtenirCommandeParIdHandler>();
        services.AddScoped<ObtenirCommandesHandler>();
        return services;
    }
}
