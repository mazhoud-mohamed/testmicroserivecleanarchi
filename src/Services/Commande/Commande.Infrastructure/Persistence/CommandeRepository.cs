using Commande.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Infrastructure.Persistence;

public class CommandeRepository(CommandeDbContext db) : ICommandeRepository
{
    public Task<CommandeAgregat?> ObtenirParIdAsync(Guid id, CancellationToken ct) =>
        db.Commandes.Include(c => c.Lignes).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<CommandeAgregat>> ObtenirToutesAsync(CancellationToken ct) =>
        await db.Commandes.Include(c => c.Lignes).AsNoTracking().ToListAsync(ct);

    public async Task AjouterAsync(CommandeAgregat commande, CancellationToken ct) =>
        await db.Commandes.AddAsync(commande, ct);

    public Task SauvegarderAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
