using Microsoft.EntityFrameworkCore;
using Stock.Domain.Articles;
using Stock.Infrastructure.Outbox;

namespace Stock.Infrastructure.Persistence;

public class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public DbSet<ArticleStock> Articles => Set<ArticleStock>();
    public DbSet<MessageTraite> MessagesTraites => Set<MessageTraite>();
    public DbSet<MessageOutbox> MessagesOutbox => Set<MessageOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArticleStock>(b =>
        {
            // La clé est le ProduitId venant du service Commande : EF ne doit pas en générer une.
            b.HasKey(a => a.ProduitId);
            b.Property(a => a.ProduitId).ValueGeneratedNever();
        });

        modelBuilder.Entity<MessageOutbox>(b =>
        {
            b.ToTable("MessagesOutbox");
            b.HasKey(m => m.Id);
            b.Property(m => m.Type).HasMaxLength(200);
            b.Property(m => m.CleRoutage).HasMaxLength(200);
            // Le publicateur cherche en permanence les lignes non traitées : on l'aide avec un index.
            b.HasIndex(m => m.TraiteLe);
        });

        modelBuilder.Entity<MessageTraite>(b =>
        {
            b.ToTable("MessagesTraites");
            // L'Id vient du message (EventId) : EF ne doit pas en générer un.
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
        });
    }
}
