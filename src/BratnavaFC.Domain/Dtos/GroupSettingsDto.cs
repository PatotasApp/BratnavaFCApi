using System;

namespace BratnavaFC.Domain.Dtos;

public sealed class GroupSettingsDto
{
    public Guid GroupId { get; init; }

    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; }

    public string? DefaultPlaceName { get; init; }

    public DayOfWeek? DefaultDayOfWeek { get; init; }
    public TimeSpan? DefaultKickoffTime { get; init; }

    public bool IsPersisted { get; init; }
}
