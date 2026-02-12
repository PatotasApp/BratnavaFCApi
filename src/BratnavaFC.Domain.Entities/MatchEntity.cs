using System;
using System.Collections.Generic;

namespace BratnavaFC.Domain.Entities;

public class MatchEntity : BaseEntity
{
    // EF Core
    private MatchEntity() { }

    public MatchEntity(DateTime playedAt)
    {
        PlayedAt = playedAt;
    }

    public DateTime PlayedAt { get; private set; }
    public int HomeGoals { get; private set; }
    public int AwayGoals { get; private set; }

    public List<MatchPlayerEntity> Players { get; private set; } = new();

    public void SetPlayedAt(DateTime playedAt)
    {
        PlayedAt = playedAt;
        UpdateDate = DateTime.UtcNow;
    }

    public void SetScore(int homeGoals, int awayGoals)
    {
        HomeGoals = homeGoals;
        AwayGoals = awayGoals;
        UpdateDate = DateTime.UtcNow;
    }

    public void AddPlayer(MatchPlayerEntity player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        player.AssignToMatch(this);
        Players.Add(player);
        UpdateDate = DateTime.UtcNow;
    }

    public bool RemovePlayer(MatchPlayerEntity player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        var removed = Players.Remove(player);
        if (removed) UpdateDate = DateTime.UtcNow;
        return removed;
    }
}
