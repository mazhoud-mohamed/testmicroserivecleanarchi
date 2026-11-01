namespace Commande.Domain.Common;

// Un fait métier important qui vient de se produire (toujours nommé au passé).
public interface IDomainEvent
{
    Guid Id { get; }
    DateTime SurvenueLe { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime SurvenueLe { get; } = DateTime.UtcNow;
}
