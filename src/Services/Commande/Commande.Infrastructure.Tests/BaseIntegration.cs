using Commande.Application;
using Commande.Infrastructure;
using Commande.Infrastructure.Messaging;
using Commande.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Commande.Infrastructure.Tests;

// Fausse sortie vers RabbitMQ : enregistre ce qu'on lui demande d'envoyer, et peut simuler une panne.
public class FauxPublicateur : IPublicateurMessages
{
    private readonly List<(Guid Id, string Cle, string Contenu)> _publies = [];
    private readonly object _verrou = new();

    public bool EnPanne { get; set; }

    public IReadOnlyList<(Guid Id, string Cle, string Contenu)> Publies
    {
        get { lock (_verrou) return _publies.ToList(); }
    }

    public Task PublierAsync(Guid messageId, string type, string cleRoutage, string contenuJson, CancellationToken ct)
    {
        if (EnPanne) throw new InvalidOperationException("RabbitMQ est en panne (simulé).");
        lock (_verrou) _publies.Add((messageId, cleRoutage, contenuJson));
        return Task.CompletedTask;
    }
}

// Base commune des tests d'intégration : une VRAIE base SQL Server, créée au début de chaque test et supprimée à la fin.
//
// Serveur : variable d'environnement COMMANDE_TESTS_SQLSERVER (par défaut MED\SQLMED, l'instance locale du projet).
// La base porte un nom unique (CommandeDb_Tests_<guid>) : elle ne touche jamais à CommandeDb.
public abstract class BaseIntegration : IAsyncLifetime
{
    private readonly string _nomBase = $"CommandeDb_Tests_{Guid.NewGuid():N}";

    protected FauxPublicateur Publicateur { get; } = new();
    protected ServiceProvider Services { get; private set; } = null!;

    private string ChaineDeConnexion()
    {
        var serveur = Environment.GetEnvironmentVariable("COMMANDE_TESTS_SQLSERVER") ?? @"MED\SQLMED";
        // Pooling=false : à la suppression, aucune connexion ne reste ouverte sur la base.
        return $"Server={serveur};Database={_nomBase};Trusted_Connection=True;TrustServerCertificate=True;Pooling=false";
    }

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:CommandeDb"] = ChaineDeConnexion() })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddInfrastructure(config);

        // On remplace uniquement l'envoi vers RabbitMQ ; tout le reste (DbContext, Outbox, dispatcher) est le vrai code.
        services.AddSingleton<IPublicateurMessages>(Publicateur);

        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        // Migrate (et non EnsureCreated) : les tests exercent les vraies migrations, pas seulement le modèle.
        await scope.ServiceProvider.GetRequiredService<CommandeDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<CommandeDbContext>().Database.EnsureDeletedAsync();
        await Services.DisposeAsync();
    }

    // Chaque opération a son propre scope, donc son propre DbContext : comme une vraie requête HTTP.
    // On relit ainsi toujours depuis la base, jamais depuis un objet resté en mémoire.
    protected async Task<T> DansUnScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected Task DansUnScope(Func<IServiceProvider, Task> action) =>
        DansUnScope(async sp => { await action(sp); return 0; });
}
