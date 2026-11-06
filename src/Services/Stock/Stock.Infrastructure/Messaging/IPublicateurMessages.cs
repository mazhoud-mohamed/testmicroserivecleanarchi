namespace Stock.Infrastructure.Messaging;

// Envoie un message DÉJÀ sérialisé vers le broker. Il ne connaît pas les types d'événements :
// il reçoit le JSON tel qu'il a été stocké dans l'Outbox.
public interface IPublicateurMessages
{
    Task PublierAsync(Guid messageId, string type, string cleRoutage, string contenuJson, CancellationToken ct);
}
