using Commande.Domain.Common;

namespace Commande.Application.Abstractions;

// Réagit à un événement de domaine. Un même événement peut avoir plusieurs handlers.
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent evenement, CancellationToken ct);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> evenements, CancellationToken ct);
}
