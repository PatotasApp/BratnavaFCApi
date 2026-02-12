using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public abstract class InactivatableEntity : BaseEntity
{
    public Status Status { get; private set; } = Status.Active;

    public DateTime? InactivatedAt { get; private set; }

    public void Inactivate()
    {
        if (Status == Status.Inactive) return;

        Status = Status.Inactive;
        InactivatedAt = DateTime.UtcNow;
    }

    public bool IsInactive()
        => Status == Status.Inactive;

    public void Reactivate()
    {
        if (Status == Status.Active) return;

        Status = Status.Active;
        InactivatedAt = null;
    }
}
