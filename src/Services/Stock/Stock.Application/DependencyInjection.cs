using Microsoft.Extensions.DependencyInjection;
using Stock.Application.Articles;

namespace Stock.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ApprovisionnerStockHandler>();
        services.AddScoped<ObtenirStocksHandler>();
        services.AddScoped<ReserverStockPourCommandeHandler>();
        return services;
    }
}
