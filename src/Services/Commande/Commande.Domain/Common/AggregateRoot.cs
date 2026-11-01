namespace Commande.Domain.Common;

// Classe de base des agrégats : elle collecte les événements en attente de publication.
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _evenements = [];

    public IReadOnlyCollection<IDomainEvent> Evenements => _evenements.AsReadOnly();

    protected void AjouterEvenement(IDomainEvent evenement) => _evenements.Add(evenement);

    public void ViderEvenements() => _evenements.Clear();
}
