using Commande.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Commande.Infrastructure.Persistence;

// Une ligne de la table MessagesTraites = un message déjà appliqué.
// L'Id est la clé primaire : la base elle-même interdit d'enregistrer deux fois le même message.
public class MessageTraite
{
    public Guid Id { get; set; }          // EventId du message
    public DateTime TraiteLe { get; set; }
}

// Scoped : même DbContext que le dépôt, donc même SaveChanges, donc même transaction.
public class MessagesTraites(CommandeDbContext db) : IMessagesTraites
{
    public Task<bool> ADejaEteTraiteAsync(Guid messageId, CancellationToken ct) =>
        db.MessagesTraites.AnyAsync(m => m.Id == messageId, ct);

    public void MarquerCommeTraite(Guid messageId) =>
        db.MessagesTraites.Add(new MessageTraite { Id = messageId, TraiteLe = DateTime.UtcNow });
}
