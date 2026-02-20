namespace BratnavaFC.Domain.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreateDate { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdateDate { get; private set; }

    // EF
    protected BaseEntity() { }
}
