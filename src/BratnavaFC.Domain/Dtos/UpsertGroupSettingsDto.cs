using System;

namespace BratnavaFC.Domain.Dtos;

public sealed class UpsertGroupSettingsDto
{
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string? DefaultPlaceName { get; set; }
    public DayOfWeek? DefaultDayOfWeek { get; set; }
    public TimeSpan? DefaultKickoffTime { get; set; }

    // ── Ícones configuráveis ─────────────────────────────────────────────────
    public string? GoalIcon       { get; set; }
    public string? GoalkeeperIcon { get; set; }
    public string? AssistIcon     { get; set; }
    public string? OwnGoalIcon    { get; set; }
    public string? MvpIcon        { get; set; }
    public string? PlayerIcon     { get; set; }
}
