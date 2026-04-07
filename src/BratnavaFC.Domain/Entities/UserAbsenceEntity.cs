using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class UserAbsenceEntity : BaseEntity
{
    // EF
    private UserAbsenceEntity() { }

    public UserAbsenceEntity(Guid userId, DateOnly startDate, DateOnly endDate, AbsenceType absenceType, string? description)
    {
        if (userId == Guid.Empty) throw new InvalidOperationException("UserId é obrigatório.");
        if (endDate < startDate)  throw new InvalidOperationException("EndDate não pode ser anterior a StartDate.");

        UserId      = userId;
        StartDate   = startDate;
        EndDate     = endDate;
        AbsenceType = absenceType;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid UserId { get; private set; }
    public UserEntity? User { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate   { get; private set; }

    public AbsenceType AbsenceType { get; private set; }
    public string? Description { get; private set; }

    public void Update(DateOnly startDate, DateOnly endDate, AbsenceType absenceType, string? description)
    {
        if (endDate < startDate) throw new InvalidOperationException("EndDate não pode ser anterior a StartDate.");

        StartDate   = startDate;
        EndDate     = endDate;
        AbsenceType = absenceType;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}
