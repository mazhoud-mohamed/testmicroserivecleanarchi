namespace Commande.Application.Abstractions;

// L'« Inbox » : la mémoire des messages déjà traités, pour ne jamais appliquer deux fois le même.
public interface IMessagesTraites
{
    Task<bool> ADejaEteTraiteAsync(Guid messageId, CancellationToken ct);

    // N'écrit rien en base : la trace est enregistrée par le prochain SauvegarderAsync du dépôt,
    // dans la même transaction que l'annulation. Les deux réussissent ou échouent ensemble.
    void MarquerCommeTraite(Guid messageId);
}
