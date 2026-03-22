using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using System.Collections.Generic;

namespace BranavaFC.Tests;

internal static class TestHelpers
{
    // ----------------------------------------------------------------
    // Player builders
    // ----------------------------------------------------------------

    public static List<PlayerRequestDto> Players(params (string name, bool gk)[] specs)
        => specs.Select((s, i) => new PlayerRequestDto(GuidFromInt(i + 1), s.name, s.gk)).ToList();

    public static Guid GuidFromInt(int n)
    {
        var bytes = new byte[16];
        bytes[15] = (byte)(n & 0xFF);
        bytes[14] = (byte)(n >> 8 & 0xFF);
        bytes[13] = (byte)(n >> 16 & 0xFF);
        bytes[12] = (byte)(n >> 24 & 0xFF);
        return new Guid(bytes);
    }

    // ----------------------------------------------------------------
    // Stats builders
    // ----------------------------------------------------------------

    /// <summary>
    /// Non-neutral stats (wins+ties+losses >= 3 by default).
    /// WinRate is set to the raw ratio here; real service uses W_base (Bayesian + GoalContrib).
    /// </summary>
    public static PlayerStats Stats(
        Guid id,
        string name,
        int wins,
        int ties,
        int losses,
        double? neutralOverride = null,
        int goals = 0,
        int assists = 0)
        => new PlayerStats
        {
            PlayerId = id,
            Name = name,
            Wins = wins,
            Ties = ties,
            Losses = losses,
            WinRate = (wins + ties + losses) == 0
                ? 0.0
                : wins / (double)(wins + ties + losses),
            Goals = goals,
            Assists = assists,
            SynergyWith = new Dictionary<Guid, double>(),
            NeutralOverride = neutralOverride
        };

    /// <summary>
    /// Neutral stats (0 matches, optional NeutralOverride for guest star rating tests).
    /// </summary>
    public static PlayerStats NeutralStats(
        Guid id,
        string name,
        double? neutralOverride = null)
        => new PlayerStats
        {
            PlayerId = id,
            Name = name,
            Wins = 0,
            Ties = 0,
            Losses = 0,
            WinRate = 0.0,
            SynergyWith = new Dictionary<Guid, double>(),
            NeutralOverride = neutralOverride
        };
}

// ----------------------------------------------------------------
// FakeStatsService
// ----------------------------------------------------------------

internal sealed class FakeStatsService : IPlayerStatsService
{
    private readonly Dictionary<Guid, PlayerStats> _byId;

    public FakeStatsService(IEnumerable<PlayerStats> stats)
    {
        _byId = stats.ToDictionary(s => s.PlayerId);
    }

    public Task<Result<List<PlayerStats>>> EnrichPlayersAsync(
        List<PlayerRequestDto> players,
        CancellationToken cancellationToken = default)
    {
        var result = players.Select(p =>
            _byId.TryGetValue(p.Id, out var s)
                ? s
                : TestHelpers.NeutralStats(p.Id, p.Name)
        ).ToList();

        return Task.FromResult(Result<List<PlayerStats>>.Ok(result));
    }

    public Task<PlayerVisualStatsReport> GetVisualReportAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PlayerVisualStatsReport
        {
            GroupId = groupId,
            TotalMatchesConsidered = 0,
            TotalFinalizedMatches = 0,
            TotalMatchesWithScore = 0,
            Players = new List<PlayerVisualStatsItem>()
        });

    public Task<PlayerSpotlightReport> GetSpotlightReportAsync(Guid groupId, CancellationToken cancellationToken = default)
        => Task.FromResult(new PlayerSpotlightReport { GroupId = groupId });
}
