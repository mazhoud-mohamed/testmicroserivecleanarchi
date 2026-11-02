using Microsoft.EntityFrameworkCore;
using Stock.Domain.Articles;

namespace Stock.Infrastructure.Persistence;

public class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public DbSet<ArticleStock> Articles => Set<ArticleStock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArticleStock>(b =>
        {
            // La clé est le ProduitId venant du service Commande : EF ne doit pas en générer une.
            b.HasKey(a => a.ProduitId);
            b.Property(a => a.ProduitId).ValueGeneratedNever();
        });
    }
}
