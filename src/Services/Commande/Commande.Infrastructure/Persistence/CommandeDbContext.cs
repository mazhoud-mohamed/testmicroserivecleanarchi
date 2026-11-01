using Commande.Domain.Commandes;
using Microsoft.EntityFrameworkCore;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Infrastructure.Persistence;

public class CommandeDbContext(DbContextOptions<CommandeDbContext> options) : DbContext(options)
{
    public DbSet<CommandeAgregat> Commandes => Set<CommandeAgregat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommandeAgregat>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Statut).HasConversion<string>().HasMaxLength(20);
            b.Ignore(c => c.MontantTotal);
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
