using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Groups;


public record GroupDto(Guid Id, string Name, DateTimeOffset? ScheduleMatchDate, Guid[] AdminIds, Guid[] FinanceiroIds, Status Status, List<PlayerDto> Players, Guid CreatedByUserId)
{
    /// <summary>Nomes dos usuários admin, na mesma ordem de AdminIds.</summary>
    public string[] AdminNames { get; init; } = Array.Empty<string>();
    /// <summary>Nomes dos usuários financeiro, na mesma ordem de FinanceiroIds.</summary>
    public string[] FinanceiroNames { get; init; } = Array.Empty<string>();
    public string? LogoUrl { get; init; }
    public DateTimeOffset? LogoUpdatedAt { get; init; }
}
