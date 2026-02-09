using System;

namespace BratnavaFC.Domain.Dtos;

public sealed class UpsertGroupSettingsDto
{
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string? DefaultPlaceName { get; set; }
    public DayOfWeek? DefaultDayOfWeek { get; set; }
    public TimeSpan? DefaultKickoffTime { get; set; }
}
