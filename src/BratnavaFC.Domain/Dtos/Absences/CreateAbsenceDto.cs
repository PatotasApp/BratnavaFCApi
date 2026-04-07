using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Absences;

public sealed record CreateAbsenceDto(
    DateOnly StartDate,
    DateOnly EndDate,
    AbsenceType AbsenceType,
    string? Description
);
