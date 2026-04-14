using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.TeamGeneration;

/// <summary>
/// Shared constants, helpers and types used by all team-generation strategies.
/// Import with <c>using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;</c>.
/// </summary>
internal static class StrategyHelpers
{
    // ── Neutral rule ──────────────────────────────────────────────────────────

    /// <summary>Players with fewer than this many matches are treated as neutral.</summary>
    internal const int MinMatchesToBeNonNeutral = 3;

    /// <summary>Win-rate used for neutral players when no <see cref="PlayerStats.NeutralOverride"/> is set.</summary>
    internal const double NeutralWinRate = 0.50;

    // ── Statistics helpers ────────────────────────────────────────────────────

    internal static int TotalMatches(PlayerStats s)
        => (s?.Wins ?? 0) + (s?.Ties ?? 0) + (s?.Losses ?? 0);

    internal static bool IsNeutral(PlayerStats s)
        => TotalMatches(s) < MinMatchesToBeNonNeutral;

    /// <summary>
    /// Effective weight (W_base) for balancing purposes.
    /// Neutral players use <see cref="PlayerStats.NeutralOverride"/> when set (populated
    /// from the guest's star-rating), otherwise fall back to <see cref="NeutralWinRate"/>.
    /// </summary>
    internal static double EffectiveWeight(PlayerStats s)
        => IsNeutral(s) ? (s.NeutralOverride ?? NeutralWinRate) : s.WinRate;

    // ── Dimensional rating accessors ──────────────────────────────────────────
    // These return 0.0 when no admin rating was set, so players without ratings
    // contribute nothing to the dimensional balance penalty.

    /// <summary>Normalized attack dimension [0, 1]. Returns 0.0 when not rated.</summary>
    internal static double AttackOf(PlayerStats s)   => s.AttackRatingNorm   ?? 0.0;

    /// <summary>Normalized defense dimension [0, 1]. Returns 0.0 when not rated.</summary>
    internal static double DefenseOf(PlayerStats s)  => s.DefenseRatingNorm  ?? 0.0;

    /// <summary>Normalized physical dimension [0, 1]. Returns 0.0 when not rated.</summary>
    internal static double PhysicalOf(PlayerStats s) => s.PhysicalRatingNorm ?? 0.0;

    // ── Candidate filtering ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the subset of players eligible for team assignment,
    /// respecting the <see cref="TeamGenerationSettings.IncludeGoalkeepers"/> flag.
    /// </summary>
    internal static List<PlayerRequestDto> FilterCandidates(
        List<PlayerRequestDto> players,
        TeamGenerationSettings settings)
        => settings.IncludeGoalkeepers
            ? players.ToList()
            : players.Where(p => !p.IsGoalkeeper).ToList();

    // ── Result builders ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the result for the degenerate case where no players can be assigned —
    /// both teams are empty and every player is unassigned.
    /// </summary>
    internal static TeamsOptionsResultDto BuildEmptyResult(List<PlayerRequestDto> players)
    {
        List<PlayerWeightDto> allUnassigned = players.Select(p => new PlayerWeightDto(p.Id, 0.0)).ToList();

        return new TeamsOptionsResultDto(
        [
            new(TeamA: [], TeamB: [], Unassigned: allUnassigned,
                TeamAWeight: 0, TeamBWeight: 0, BalanceDiff: 0,
                SynergyTotal: 0, Score: 0)
        ]);
    }

    // ── Team key ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Canonical, order-independent team key — A-vs-B produces the same key as B-vs-A,
    /// used to deduplicate equivalent options.
    /// </summary>
    internal static string BuildTeamsKey(IEnumerable<Guid> teamAIds, IEnumerable<Guid> teamBIds)
    {
        Guid[] a = teamAIds.OrderBy(x => x).ToArray();
        Guid[] b = teamBIds.OrderBy(x => x).ToArray();

        string key1 = "A:" + string.Join(",", a) + "|B:" + string.Join(",", b);
        string key2 = "A:" + string.Join(",", b) + "|B:" + string.Join(",", a);

        return string.CompareOrdinal(key1, key2) <= 0 ? key1 : key2;
    }

    // ── Stats loading ─────────────────────────────────────────────────────────

    /// <summary>Loads stats for each player and returns them keyed by PlayerId.</summary>
    internal static async Task<Result<Dictionary<Guid, PlayerStats>>> LoadStatsByPlayerId(
        IPlayerStatsService statsService,
        List<PlayerRequestDto> players,
        CancellationToken cancellationToken = default)
    {
        Result<List<PlayerStats>> statsResult = await statsService
            .EnrichPlayersAsync(players, cancellationToken)
            .ConfigureAwait(false);

        if (!statsResult.Success)
            return Result<Dictionary<Guid, PlayerStats>>.Fail(statsResult.Error!, statsResult.Status);

        return Result<Dictionary<Guid, PlayerStats>>.Ok(
            statsResult.Data!.ToDictionary(s => s.PlayerId, s => s));
    }

    /// <summary>
    /// Returns loaded stats for <paramref name="playerId"/>,
    /// or a zeroed-out placeholder if the player has no recorded history.
    /// </summary>
    internal static PlayerStats GetOrCreateStats(
        Dictionary<Guid, PlayerStats> statsById,
        Guid playerId,
        string? name)
        => statsById.TryGetValue(playerId, out PlayerStats? s)
            ? s
            : new PlayerStats
            {
                PlayerId    = playerId,
                Name        = name ?? string.Empty,
                WinRate     = 0.0,
                SynergyWith = []
            };
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A player paired with their loaded (or synthesised) stats.
/// Used internally by strategies during the draft / assignment process.
/// </summary>
internal sealed class CandidatePlayer
{
    public PlayerRequestDto Player { get; }
    public PlayerStats      Stats  { get; }

    public CandidatePlayer(PlayerRequestDto player, PlayerStats stats)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Stats  = stats  ?? throw new ArgumentNullException(nameof(stats));
    }
}
