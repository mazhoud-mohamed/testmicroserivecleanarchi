using Commande.Application.Abstractions;
using Commande.Domain.Commandes;
using Commande.Domain.Common;
using Commande.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Infrastructure.Persistence;

public class CommandeDbContext(DbContextOptions<CommandeDbContext> options, IDomainEventDispatcher dispatcher)
    : DbContext(options)
{
    public DbSet<CommandeAgregat> Commandes => Set<CommandeAgregat>();
    public DbSet<MessageOutbox> MessagesOutbox => Set<MessageOutbox>();
    public DbSet<MessageTraite> MessagesTraites => Set<MessageTraite>();

    // ÉTAPE 2 du trajet : EF Core appelle cette méthode quand on sauvegarde.
    // On publie les événements de domaine juste AVANT d'écrire en base : les handlers participent
    // ainsi à la même transaction (utile pour l'Outbox plus tard).
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // 1) Parmi les entités suivies par EF, on prend les agrégats qui ont des événements en attente.
        var agregats = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.Evenements.Count > 0)
            .ToList();

        // 2) On copie les événements puis on vide les listes : un événement n'est publié qu'une seule fois.
        var evenements = agregats.SelectMany(a => a.Evenements).ToList();
        agregats.ForEach(a => a.ViderEvenements());

        // 3) On envoie aux handlers, puis 4) seulement après on écrit vraiment en base.
        await dispatcher.DispatchAsync(evenements, ct);
        return await base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommandeAgregat>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.MotifAnnulation).HasMaxLength(1000);
            b.Ignore(c => c.MontantTotal);
            b.Ignore(c => c.Evenements);
            b.HasMany(c => c.Lignes).WithOne().OnDelete(DeleteBehavior.Cascade);
            // EF remplit la liste privée _lignes au lieu de passer par la propriété en lecture seule.
            b.Navigation(c => c.Lignes).UsePropertyAccessMode(PropertyAccessMode.Field);
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

        modelBuilder.Entity<LigneCommande>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.NomProduit).HasMaxLength(200);
            b.Property(l => l.PrixUnitaire).HasPrecision(18, 2);
            b.Ignore(l => l.Total);
        });
    }
}
