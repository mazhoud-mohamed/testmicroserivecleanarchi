using Commande.Application.Abstractions;
using Commande.Domain.Commandes;
using Commande.Domain.Common;
using Microsoft.EntityFrameworkCore;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Infrastructure.Persistence;

public class CommandeDbContext(DbContextOptions<CommandeDbContext> options, IDomainEventDispatcher dispatcher)
    : DbContext(options)
{
    public DbSet<CommandeAgregat> Commandes => Set<CommandeAgregat>();

    // Publie les événements de domaine juste avant d'écrire en base : les handlers participent à la même transaction.
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var agregats = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.Evenements.Count > 0)
            .ToList();

        var evenements = agregats.SelectMany(a => a.Evenements).ToList();
        agregats.ForEach(a => a.ViderEvenements());

        await dispatcher.DispatchAsync(evenements, ct);
        return await base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommandeAgregat>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Statut).HasConversion<string>().HasMaxLength(20);
            b.Ignore(c => c.MontantTotal);
            b.Ignore(c => c.Evenements);
            b.HasMany(c => c.Lignes).WithOne().OnDelete(DeleteBehavior.Cascade);
            // EF remplit la liste privée _lignes au lieu de passer par la propriété en lecture seule.
            b.Navigation(c => c.Lignes).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<LigneCommande>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.NomProduit).HasMaxLength(200);
            b.Property(l => l.PrixUnitaire).HasPrecision(18, 2);
            b.Ignore(l => l.Total);
        });
    }
}
