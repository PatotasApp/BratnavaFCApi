using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BranavaFC.Tests;

public class AlgorithmStrategyTests
{
    [Fact]
    public async Task AlgorithmStrategy_Balances_12Players_2Goalkeepers_Realistic()
    {
        var players = NewPlayersDeterministic(
            ("GK1", true), ("GK2", true),
            ("P1", false), ("P2", false), ("P3", false), ("P4", false), ("P5", false),
            ("P6", false), ("P7", false), ("P8", false), ("P9", false), ("P10", false)
        );

        var id = players.ToDictionary(p => p.Name, p => p.Id);

        var stats = new Dictionary<Guid, PlayerStats>
        {
            [id["GK1"]] = PS(id["GK1"], "GK1", wins: 12, winRate: 0.54),
            [id["GK2"]] = PS(id["GK2"], "GK2", wins: 10, winRate: 0.50),

            [id["P1"]] = PS(id["P1"], "P1", wins: 18, winRate: 0.70),
            [id["P2"]] = PS(id["P2"], "P2", wins: 16, winRate: 0.66),
            [id["P3"]] = PS(id["P3"], "P3", wins: 14, winRate: 0.62),
            [id["P4"]] = PS(id["P4"], "P4", wins: 13, winRate: 0.58),
            [id["P5"]] = PS(id["P5"], "P5", wins: 12, winRate: 0.55),
            [id["P6"]] = PS(id["P6"], "P6", wins: 11, winRate: 0.52),
            [id["P7"]] = PS(id["P7"], "P7", wins: 10, winRate: 0.48),
            [id["P8"]] = PS(id["P8"], "P8", wins: 9, winRate: 0.45),
            [id["P9"]] = PS(id["P9"], "P9", wins: 7, winRate: 0.40),
            [id["P10"]] = PS(id["P10"], "P10", wins: 6, winRate: 0.36),
        };

        AddSynergy(stats, id["P1"], id["P3"], 0.82);
        AddSynergy(stats, id["P2"], id["P4"], 0.75);
        AddSynergy(stats, id["P5"], id["P6"], 0.70);
        AddSynergy(stats, id["P7"], id["P8"], 0.68);
        AddSynergy(stats, id["GK1"], id["P2"], 0.62);
        AddSynergy(stats, id["GK2"], id["P6"], 0.60);

        var fake = new FakeStatsService(stats);
        var strategy = new AlgorithmStrategy(fake);

        var settings = new TeamGenerationSettings { PlayersPerTeam = 6, IncludeGoalkeepers = true };

        var result = await strategy.GenerateTeamsAsync(players, settings);

        Assert.Equal(6, result.TeamA.Count);
        Assert.Equal(6, result.TeamB.Count);
        Assert.Empty(result.Unassigned);

        var gkA = CountGoalkeepers(players, result.TeamA);
        var gkB = CountGoalkeepers(players, result.TeamB);
        Assert.True(gkA == 1 && gkB == 1,
            $"Expected 1 GK per team. TeamA GK={gkA}, TeamB GK={gkB}\n" +
            DumpTeams(players, stats, result));

        AssertBalancedByWinRateSum(result, stats, maxDiff: 0.25);
    }

    [Fact]
    public async Task AlgorithmStrategy_Prefers_Strong_Synergy_When_Balance_Allows_12Players()
    {
        var players = NewPlayersDeterministic(
            ("GK1", true), ("GK2", true),
            ("A", false), ("B", false), ("C", false), ("D", false), ("E", false),
            ("F", false), ("G", false), ("H", false), ("I", false), ("J", false)
        );

        var id = players.ToDictionary(p => p.Name, p => p.Id);

        var stats = new Dictionary<Guid, PlayerStats>
        {
            [id["GK1"]] = PS(id["GK1"], "GK1", wins: 12, winRate: 0.53),
            [id["GK2"]] = PS(id["GK2"], "GK2", wins: 10, winRate: 0.49),

            [id["A"]] = PS(id["A"], "A", wins: 18, winRate: 0.71),
            [id["B"]] = PS(id["B"], "B", wins: 16, winRate: 0.66),
            [id["C"]] = PS(id["C"], "C", wins: 14, winRate: 0.62),
            [id["D"]] = PS(id["D"], "D", wins: 13, winRate: 0.58),
            [id["E"]] = PS(id["E"], "E", wins: 12, winRate: 0.55),
            [id["F"]] = PS(id["F"], "F", wins: 11, winRate: 0.52),
            [id["G"]] = PS(id["G"], "G", wins: 10, winRate: 0.48),
            [id["H"]] = PS(id["H"], "H", wins: 9, winRate: 0.45),
            [id["I"]] = PS(id["I"], "I", wins: 7, winRate: 0.40),
            [id["J"]] = PS(id["J"], "J", wins: 6, winRate: 0.36),
        };

        AddSynergy(stats, id["A"], id["C"], 0.85);
        AddSynergy(stats, id["B"], id["D"], 0.78);
        AddSynergy(stats, id["E"], id["F"], 0.65);

        var fake = new FakeStatsService(stats);
        var strategy = new AlgorithmStrategy(fake);

        var settings = new TeamGenerationSettings { PlayersPerTeam = 6, IncludeGoalkeepers = true };
        var result = await strategy.GenerateTeamsAsync(players, settings);

        Assert.Equal(6, result.TeamA.Count);
        Assert.Equal(6, result.TeamB.Count);
        Assert.Empty(result.Unassigned);

        var gkA = CountGoalkeepers(players, result.TeamA);
        var gkB = CountGoalkeepers(players, result.TeamB);
        Assert.True(gkA == 1 && gkB == 1,
            $"Expected 1 GK per team. TeamA GK={gkA}, TeamB GK={gkB}\n" +
            DumpTeams(players, stats, result));

        AssertBalancedByWinRateSum(result, stats, maxDiff: 0.30);

        var teamA = result.TeamA.ToHashSet();
        var teamB = result.TeamB.ToHashSet();

        bool aWithC =
            teamA.Contains(id["A"]) && teamA.Contains(id["C"]) ||
            teamB.Contains(id["A"]) && teamB.Contains(id["C"]);

        bool bWithD =
            teamA.Contains(id["B"]) && teamA.Contains(id["D"]) ||
            teamB.Contains(id["B"]) && teamB.Contains(id["D"]);

        Assert.True(aWithC || bWithD,
            "Expected at least one strong synergy pair to be kept together (A+C or B+D).\n" +
            DumpTeams(players, stats, result));
    }

    // ----------------- helpers -----------------

    private static List<PlayerRequestDto> NewPlayersDeterministic(params (string name, bool isGk)[] specs)
    {
        int i = 1;
        return specs.Select(s => new PlayerRequestDto(GuidFromInt(i++), s.name, s.isGk)).ToList();
    }

    private static Guid GuidFromInt(int n)
    {
        var bytes = new byte[16];
        bytes[15] = (byte)(n & 0xFF);
        bytes[14] = (byte)(n >> 8 & 0xFF);
        bytes[13] = (byte)(n >> 16 & 0xFF);
        bytes[12] = (byte)(n >> 24 & 0xFF);
        return new Guid(bytes);
    }

    private static PlayerStats PS(Guid playerId, string name, int wins, double winRate)
        => new PlayerStats
        {
            PlayerId = playerId,
            Name = name,
            Wins = wins,
            Ties = 0,
            Losses = 0,
            WinRate = winRate,
            SynergyWith = new Dictionary<Guid, double>()
        };

    private static void AddSynergy(Dictionary<Guid, PlayerStats> stats, Guid a, Guid b, double value01)
    {
        if (!stats.TryGetValue(a, out var sa) || !stats.TryGetValue(b, out var sb))
            return;

        sa.SynergyWith[b] = value01;
        sb.SynergyWith[a] = value01;
    }

    private static int CountGoalkeepers(List<PlayerRequestDto> players, List<Guid> teamIds)
    {
        var lookup = players.ToDictionary(p => p.Id, p => p);
        return teamIds.Count(id => lookup[id].IsGoalkeeper);
    }

    private static void AssertBalancedByWinRateSum(
        TeamsResultDto result,
        Dictionary<Guid, PlayerStats> stats,
        double maxDiff)
    {
        double sumA = result.TeamA.Sum(id => stats[id].WinRate);
        double sumB = result.TeamB.Sum(id => stats[id].WinRate);
        var diff = Math.Abs(sumA - sumB);

        Assert.True(diff <= maxDiff,
            $"Teams not balanced enough by WinRate sum. TeamA={sumA:0.000}, TeamB={sumB:0.000}, diff={diff:0.000}, maxDiff={maxDiff:0.000}\n" +
            DumpTeams(null, stats, result));
    }

    private static string DumpTeams(List<PlayerRequestDto>? players, Dictionary<Guid, PlayerStats> stats, TeamsResultDto result)
    {
        var nameById = players?.ToDictionary(p => p.Id, p => p.Name)
                      ?? stats.ToDictionary(k => k.Key, v => v.Value.Name);

        string fmt(List<Guid> ids) =>
            string.Join(", ", ids.Select(id =>
            {
                var s = stats[id];
                var name = nameById.TryGetValue(id, out var n) ? n : id.ToString();
                return $"{name}(wr={s.WinRate:0.00})";
            }));

        return
            $"TeamA: {fmt(result.TeamA)}\n" +
            $"TeamB: {fmt(result.TeamB)}\n" +
            (result.Unassigned.Count > 0 ? $"Unassigned: {fmt(result.Unassigned)}\n" : "");
    }

    private sealed class FakeStatsService : IPlayerStatsService
    {
        private readonly Dictionary<Guid, PlayerStats> _stats;

        public FakeStatsService(Dictionary<Guid, PlayerStats> stats)
        {
            _stats = stats ?? new Dictionary<Guid, PlayerStats>();
        }

        public Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerRequestDto> players, CancellationToken cancellationToken = default)
        {
            var list = players.Select(p =>
            {
                if (_stats.TryGetValue(p.Id, out var s))
                    return s;

                return new PlayerStats
                {
                    PlayerId = p.Id,
                    Name = p.Name,
                    Wins = 0,
                    Ties = 0,
                    Losses = 0,
                    WinRate = 0.0,
                    SynergyWith = new Dictionary<Guid, double>()
                };
            }).ToList();

            return Task.FromResult(list);
        }

        public Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlayerVisualStatsReport
            {
                GroupId = groupId,
                TotalMatchesConsidered = 0,
                TotalFinalizedMatches = 0,
                TotalMatchesWithScore = 0,
                Players = new List<PlayerVisualStatsItem>()
            });
    }
}
