using Commande.Domain.Common;

namespace Commande.Domain.Commandes.Evenements;

public record CommandeConfirmee(Guid CommandeId, Guid ClientId, decimal MontantTotal) : DomainEvent;

public record CommandeExpediee(Guid CommandeId, Guid ClientId) : DomainEvent;

public record CommandeAnnulee(Guid CommandeId, Guid ClientId) : DomainEvent;
