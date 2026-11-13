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

            // CONCURRENCE OPTIMISTE : SQL Server met à jour tout seul cette colonne à chaque modification de la ligne.
            // EF l'ajoute à chaque UPDATE (« ... WHERE ProduitId = @p AND RowVersion = @valeurLue ») :
            // si quelqu'un a modifié la ligne entre notre lecture et notre écriture, 0 ligne est touchée
            // et EF lève DbUpdateConcurrencyException au lieu d'écraser son travail.
            // Propriété d'ombre : elle n'existe que pour EF, la classe du Domain n'en sait rien.
            b.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<MessageOutbox>(b =>
        {
            b.ToTable("MessagesOutbox");
            b.HasKey(m => m.Id);
            b.Property(m => m.Type).HasMaxLength(200);
            b.Property(m => m.CleRoutage).HasMaxLength(200);
            b.Property(m => m.TraceParent).HasMaxLength(100); // « traceparent » W3C : 55 caractères
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
