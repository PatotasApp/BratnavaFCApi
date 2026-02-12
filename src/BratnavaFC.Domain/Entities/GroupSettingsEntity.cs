using System;

namespace BratnavaFC.Domain.Entities;

public sealed class GroupSettingsEntity : BaseEntity
{
    private GroupSettingsEntity() { } // EF

    public GroupSettingsEntity(
        Guid groupId,
        int minPlayers,
        int maxPlayers,
        string? defaultPlaceName,
        DayOfWeek? defaultDayOfWeek,
        TimeSpan? defaultKickoffTime)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId e obrigatorio.");

        GroupId = groupId;

        SetPlayerLimits(minPlayers, maxPlayers);
        SetDefaultPlaceName(defaultPlaceName);
        SetDefaultSchedule(defaultDayOfWeek, defaultKickoffTime);
    }

    public Guid GroupId { get; private set; }

    public int MinPlayers { get; private set; }
    public int MaxPlayers { get; private set; }

    public string? DefaultPlaceName { get; private set; }

    public DayOfWeek? DefaultDayOfWeek { get; private set; }
    public TimeSpan? DefaultKickoffTime { get; private set; }

    public void Update(
        int minPlayers,
        int maxPlayers,
        string? defaultPlaceName,
        DayOfWeek? defaultDayOfWeek,
        TimeSpan? defaultKickoffTime)
    {
        SetPlayerLimits(minPlayers, maxPlayers);
        SetDefaultPlaceName(defaultPlaceName);
        SetDefaultSchedule(defaultDayOfWeek, defaultKickoffTime);
    }

    private void SetPlayerLimits(int minPlayers, int maxPlayers)
    {
        if (minPlayers <= 0)
            throw new InvalidOperationException("MinPlayers deve ser maior que 0.");

        if (maxPlayers <= 0)
            throw new InvalidOperationException("MaxPlayers deve ser maior que 0.");

        if (minPlayers > maxPlayers)
            throw new InvalidOperationException("MinPlayers nao pode ser maior que MaxPlayers.");

        if (maxPlayers > 22)
            throw new InvalidOperationException("MaxPlayers nao pode ser maior que 22.");

        MinPlayers = minPlayers;
        MaxPlayers = maxPlayers;
    }

    private void SetDefaultPlaceName(string? placeName)
    {
        DefaultPlaceName = string.IsNullOrWhiteSpace(placeName) ? null : placeName.Trim();
    }

    private void SetDefaultSchedule(DayOfWeek? dayOfWeek, TimeSpan? kickoffTime)
    {
        if (kickoffTime.HasValue)
        {
            if (kickoffTime.Value < TimeSpan.Zero || kickoffTime.Value >= TimeSpan.FromDays(1))
                throw new InvalidOperationException("DefaultKickoffTime invalido.");
        }

        DefaultDayOfWeek = dayOfWeek;
        DefaultKickoffTime = kickoffTime;
    }
}
