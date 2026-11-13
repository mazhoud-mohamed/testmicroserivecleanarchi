using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stock.Application;
using Stock.Infrastructure.Messaging;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure.Tests;

// Fausse sortie vers RabbitMQ : enregistre ce qu'on lui demande d'envoyer, et peut simuler une panne.
public class FauxPublicateur : IPublicateurMessages
{
    private readonly List<(Guid Id, string Type, string Cle, string Contenu)> _publies = [];
    private readonly object _verrou = new();

    public bool EnPanne { get; set; }

    // TraceId actif au moment de chaque publication : prouve que la trace d'origine a été restaurée par le publicateur.
    private readonly List<string?> _traceIds = [];
    public IReadOnlyList<string?> TraceIds
    {
        get { lock (_verrou) return _traceIds.ToList(); }
    }

    public IReadOnlyList<(Guid Id, string Type, string Cle, string Contenu)> Publies
    {
        get { lock (_verrou) return _publies.ToList(); }
    }

    public Task PublierAsync(Guid messageId, string type, string cleRoutage, string contenuJson, CancellationToken ct)
    {
        if (EnPanne) throw new InvalidOperationException("RabbitMQ est en panne (simulé).");
        lock (_verrou)
        {
            _publies.Add((messageId, type, cleRoutage, contenuJson));
            _traceIds.Add(Activity.Current?.TraceId.ToString());
        }
        return Task.CompletedTask;
    }
}

// Base commune des tests d'intégration de Stock : une VRAIE base SQL Server, créée au début de chaque test
// (avec les vraies migrations) et supprimée à la fin.
//
// Serveur : variable d'environnement STOCK_TESTS_SQLSERVER (par défaut MED\SQLMED, l'instance locale du projet).
// La base porte un nom unique (StockDb_Tests_<guid>) : elle ne touche jamais à StockDb.
public abstract class BaseIntegration : IAsyncLifetime
{
    private readonly string _nomBase = $"StockDb_Tests_{Guid.NewGuid():N}";

    protected FauxPublicateur Publicateur { get; } = new();
    protected ServiceProvider Services { get; private set; } = null!;

    private string ChaineDeConnexion()
    {
        var serveur = Environment.GetEnvironmentVariable("STOCK_TESTS_SQLSERVER") ?? @"MED\SQLMED";
        // Pooling=false : à la suppression, aucune connexion ne reste ouverte sur la base.
        return $"Server={serveur};Database={_nomBase};Trusted_Connection=True;TrustServerCertificate=True;Pooling=false";
    }

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:StockDb"] = ChaineDeConnexion() })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddInfrastructure(config);

        // On remplace uniquement l'envoi vers RabbitMQ ; tout le reste (DbContext, Outbox, Inbox) est le vrai code.
        services.AddSingleton<IPublicateurMessages>(Publicateur);

        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        // Migrate (et non EnsureCreated) : les tests exercent les vraies migrations.
        await scope.ServiceProvider.GetRequiredService<StockDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<StockDbContext>().Database.EnsureDeletedAsync();
        await Services.DisposeAsync();
    }

    // Chaque opération a son propre scope, donc son propre DbContext : comme un vrai message reçu.
    // On relit ainsi toujours depuis la base, jamais depuis un objet resté en mémoire.
    protected async Task<T> DansUnScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected Task DansUnScope(Func<IServiceProvider, Task> action) =>
        DansUnScope(async sp => { await action(sp); return 0; });
}
