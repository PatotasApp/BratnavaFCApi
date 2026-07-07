namespace BratnavaFC.Domain.Dtos.Absences;

public sealed class AbsenceDto
{
    public Guid Id { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int AbsenceType { get; set; }
    public string AbsenceTypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class GroupMemberAbsenceDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public List<AbsenceDto> Absences { get; set; } = [];
}

/// <summary>Ausência achatada com dados do jogador — usada na listagem paginada por data.</summary>
public sealed class GroupAbsenceItemDto
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int AbsenceType { get; set; }
    public string AbsenceTypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
