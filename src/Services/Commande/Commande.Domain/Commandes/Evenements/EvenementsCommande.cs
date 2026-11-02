using Commande.Domain.Common;

namespace Commande.Domain.Commandes.Evenements;

public record LigneConfirmee(Guid ProduitId, int Quantite);

public record CommandeConfirmee(Guid CommandeId, Guid ClientId, decimal MontantTotal, IReadOnlyList<LigneConfirmee> Lignes)
    : DomainEvent;

public record CommandeExpediee(Guid CommandeId, Guid ClientId) : DomainEvent;

public record CommandeAnnulee(Guid CommandeId, Guid ClientId) : DomainEvent;
