using System.Diagnostics;
using System.Text.Json;
using Contrats;
using Stock.Application.Abstractions;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure.Outbox;

// Implémentation de IOutbox : ajoute une ligne MessageOutbox au DbContext.
// Scoped, donc c'est le MÊME DbContext que celui qui sauvegarde le stock : une seule transaction.
public class OutboxEcriture(StockDbContext db) : IOutbox
{
    public void Ajouter<TEvent>(TEvent evenement, string cleRoutage) where TEvent : IIntegrationEvent
    {
        // Add ne touche pas la base : la ligne est seulement suivie par EF jusqu'au prochain SaveChanges.
        db.MessagesOutbox.Add(new MessageOutbox
        {
            Id = evenement.EventId,
            Type = typeof(TEvent).Name,
            CleRoutage = cleRoutage,
            Contenu = JsonSerializer.Serialize(evenement),
            CreeLe = DateTime.UtcNow,
            // Le span en cours (la requête HTTP, ou le traitement d'un message) : on le garde pour continuer la trace.
            TraceParent = Activity.Current?.Id
        });
    }
}
