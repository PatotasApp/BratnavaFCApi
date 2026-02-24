using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Xunit;

namespace BranavaFC.Tests;

public class AlgorithmStrategyTests
{
    //[Fact]
    //public async Task AlgorithmStrategy_Returns_3Options_WithWeights_And_1GKPerTeam_When2GKs()
    //{
    //    // Arrange
    //    var players = NewPlayersDeterministic(
    //        ("GK1", true), ("GK2", true),
    //        ("P1", false), ("P2", false), ("P3", false), ("P4", false), ("P5", false),
    //        ("P6", false), ("P7", false), ("P8", false), ("P9", false), ("P10", false)
    //    );

    //    var id = players.ToDictionary(p => p.Name, p => p.Id);

    //    var stats = new Dictionary<Guid, PlayerStats>
    //    {
    //        // GKs
    //        [id["GK1"]] = PS(id["GK1"], "GK1", wins: 12, ties: 0, losses: 0, winRate: 0.54),
    //        [id["GK2"]] = PS(id["GK2"], "GK2", wins: 10, ties: 0, losses: 0, winRate: 0.50),

    //        // Linha
    //        [id["P1"]] = PS(id["P1"], "P1", wins: 18, ties: 0, losses: 0, winRate: 0.70),
    //        [id["P2"]] = PS(id["P2"], "P2", wins: 16, ties: 0, losses: 0, winRate: 0.66),
    //        [id["P3"]] = PS(id["P3"], "P3", wins: 14, ties: 0, losses: 0, winRate: 0.62),
    //        [id["P4"]] = PS(id["P4"], "P4", wins: 13, ties: 0, losses: 0, winRate: 0.58),
    //        [id["P5"]] = PS(id["P5"], "P5", wins: 12, ties: 0, losses: 0, winRate: 0.55),
    //        [id["P6"]] = PS(id["P6"], "P6", wins: 11, ties: 0, losses: 0, winRate: 0.52),
    //        [id["P7"]] = PS(id["P7"], "P7", wins: 10, ties: 0, losses: 0, winRate: 0.48),
    //        [id["P8"]] = PS(id["P8"], "P8", wins: 9, ties: 0, losses: 0, winRate: 0.45),
    //        [id["P9"]] = PS(id["P9"], "P9", wins: 7, ties: 0, losses: 0, winRate: 0.40),
    //        [id["P10"]] = PS(id["P10"], "P10", wins: 6, ties: 0, losses: 0, winRate: 0.36),
    //    };

    //    AddSynergy(stats, id["P1"], id["P3"], 0.82);
    //    AddSynergy(stats, id["P2"], id["P4"], 0.75);
    //    AddSynergy(stats, id["P5"], id["P6"], 0.70);
    //    AddSynergy(stats, id["P7"], id["P8"], 0.68);
    //    AddSynergy(stats, id["GK1"], id["P2"], 0.62);
    //    AddSynergy(stats, id["GK2"], id["P6"], 0.60);

    //    var fake = new FakeStatsService(stats);
    //    var strategy = new AlgorithmStrategy(fake);

    //    var settings = new TeamGenerationSettings
    //    {
    //        PlayersPerTeam = 6,
    //        IncludeGoalkeepers = true,
    //    };

    //    // Act
    //    var result = await strategy.GenerateTeamsAsync(players, settings);

    //    // Assert (geral)
    //    Assert.NotNull(result);
    //    Assert.NotNull(result.Options);
    //    Assert.Equal(3, result.Options.Count);

    //    // Assert (cada opção consistente)
    //    foreach (var opt in result.Options)
    //    {
    //        Assert.NotNull(opt.TeamA);
    //        Assert.NotNull(opt.TeamB);

    //        Assert.Equal(6, opt.TeamA.Count);
    //        Assert.Equal(6, opt.TeamB.Count);

    //        // sem duplicar player entre A e B
    //        var all = opt.TeamA.Select(x => x.PlayerId).Concat(opt.TeamB.Select(x => x.PlayerId)).ToList();
    //        Assert.Equal(all.Count, all.Distinct().Count());

    //        // tem peso por jogador
    //        Assert.All(opt.TeamA, p => Assert.True(p.Weight > 0, $"Weight inválido em TeamA: {p.PlayerId} ({p.Weight})"));
    //        Assert.All(opt.TeamB, p => Assert.True(p.Weight > 0, $"Weight inválido em TeamB: {p.PlayerId} ({p.Weight})"));

    //        // tem peso total do time (seu DTO)
    //        Assert.True(opt.TeamAWeight > 0);
    //        Assert.True(opt.TeamBWeight > 0);

    //        // BalanceDiff bate com os pesos totais
    //        var expectedDiff = Math.Abs(opt.TeamAWeight - opt.TeamBWeight);
    //        Assert.True(Math.Abs(opt.BalanceDiff - expectedDiff) < 0.0001,
    //            $"BalanceDiff inconsistente. DTO={opt.BalanceDiff:0.0000} esperado={expectedDiff:0.0000}\n{DumpOption(opt)}");
    //    }

    //    // Assert (ordenação por Score)
    //    Assert.True(IsNonDecreasing(result.Options.Select(o => o.Score)),
    //        "Options deveriam vir ordenadas por Score asc.");
    //}

    //[Fact]
    //public async Task AlgorithmStrategy_Uses_EffectiveWinRate_Neutral_WhenLowMatches()
    //{
    //    // Arrange: P1 tem 1 partida => efetivo deve virar 0.50 (neutro)
    //    var players = NewPlayersDeterministic(
    //        ("GK1", true), ("GK2", true),
    //        ("P1", false), ("P2", false), ("P3", false), ("P4", false),
    //        ("P5", false), ("P6", false), ("P7", false), ("P8", false), ("P9", false), ("P10", false)
    //    );

    //    var id = players.ToDictionary(p => p.Name, p => p.Id);

    //    var stats = new Dictionary<Guid, PlayerStats>
    //    {
    //        [id["GK1"]] = PS(id["GK1"], "GK1", wins: 10, ties: 0, losses: 0, winRate: 0.55),
    //        [id["GK2"]] = PS(id["GK2"], "GK2", wins: 10, ties: 0, losses: 0, winRate: 0.50),

    //        // P1: apenas 1 jogo total (wins=1) mas winrate “alto” -> deve ser neutralizado p/ 0.50
    //        [id["P1"]] = PS(id["P1"], "P1", wins: 1, ties: 0, losses: 0, winRate: 0.95),

    //        // resto normal
    //        [id["P2"]] = PS(id["P2"], "P2", wins: 10, ties: 0, losses: 0, winRate: 0.60),
    //        [id["P3"]] = PS(id["P3"], "P3", wins: 10, ties: 0, losses: 0, winRate: 0.58),
    //        [id["P4"]] = PS(id["P4"], "P4", wins: 10, ties: 0, losses: 0, winRate: 0.56),
    //        [id["P5"]] = PS(id["P5"], "P5", wins: 10, ties: 0, losses: 0, winRate: 0.54),
    //        [id["P6"]] = PS(id["P6"], "P6", wins: 10, ties: 0, losses: 0, winRate: 0.52),
    //        [id["P7"]] = PS(id["P7"], "P7", wins: 10, ties: 0, losses: 0, winRate: 0.50),
    //        [id["P8"]] = PS(id["P8"], "P8", wins: 10, ties: 0, losses: 0, winRate: 0.48),
    //        [id["P9"]] = PS(id["P9"], "P9", wins: 10, ties: 0, losses: 0, winRate: 0.46),
    //        [id["P10"]] = PS(id["P10"], "P10", wins: 10, ties: 0, losses: 0, winRate: 0.44),
    //    };

    //    var fake = new FakeStatsService(stats);
    //    var strategy = new AlgorithmStrategy(fake);

    //    var settings = new TeamGenerationSettings { PlayersPerTeam = 6, IncludeGoalkeepers = true };

    //    // Act
    //    var result = await strategy.GenerateTeamsAsync(players, settings);

    //    // Assert: em alguma opção, P1 deve aparecer com EffectiveWinRate = 0.50
    //    var p1Seen = result.Options
    //        .SelectMany(o => o.TeamA.Concat(o.TeamB))
    //        .FirstOrDefault(p => p.PlayerId == id["P1"]);

    //    Assert.NotNull(p1Seen);
    //}

    //// ----------------- helpers -----------------

    //private static List<PlayerRequestDto> NewPlayersDeterministic(params (string name, bool isGk)[] specs)
    //{
    //    int i = 1;
    //    return specs.Select(s => new PlayerRequestDto(GuidFromInt(i++), s.name, s.isGk)).ToList();
    //}

    //private static Guid GuidFromInt(int n)
    //{
    //    var bytes = new byte[16];
    //    bytes[15] = (byte)(n & 0xFF);
    //    bytes[14] = (byte)(n >> 8 & 0xFF);
    //    bytes[13] = (byte)(n >> 16 & 0xFF);
    //    bytes[12] = (byte)(n >> 24 & 0xFF);
    //    return new Guid(bytes);
    //}

    //private static PlayerStats PS(Guid playerId, string name, int wins, int ties, int losses, double winRate)
    //    => new PlayerStats
    //    {
    //        PlayerId = playerId,
    //        Name = name,
    //        Wins = wins,
    //        Ties = ties,
    //        Losses = losses,
    //        WinRate = winRate,
    //        SynergyWith = new Dictionary<Guid, double>()
    //    };

    //private static void AddSynergy(Dictionary<Guid, PlayerStats> stats, Guid a, Guid b, double value01)
    //{
    //    if (!stats.TryGetValue(a, out var sa) || !stats.TryGetValue(b, out var sb))
    //        return;

    //    sa.SynergyWith[b] = value01;
    //    sb.SynergyWith[a] = value01;
    //}

    //private static bool IsNonDecreasing(IEnumerable<double> xs)
    //{
    //    double? prev = null;
    //    foreach (var x in xs)
    //    {
    //        if (prev.HasValue && x < prev.Value - 1e-12) return false;
    //        prev = x;
    //    }
    //    return true;
    //}

    //private static string DumpOption(dynamic opt)
    //{
    //    string fmt(IEnumerable<dynamic> ps) =>
    //        string.Join(", ", ps.Select(p => $"{p.Name}(w={p.Wins},wr={p.WinRate:0.00},eff={p.EffectiveWinRate:0.00},wt={p.Weight:0.000})"));

    //    return
    //        $"Score={opt.Score:0.000} BalanceDiff={opt.BalanceDiff:0.000} " +
    //        $"TeamAWeight={opt.TeamAWeight:0.000} TeamBWeight={opt.TeamBWeight:0.000}\n" +
    //        $"TeamA: {fmt(opt.TeamA)}\n" +
    //        $"TeamB: {fmt(opt.TeamB)}\n";
    //}

    //private sealed class FakeStatsService : IPlayerStatsService
    //{
    //    private readonly Dictionary<Guid, PlayerStats> _stats;

    //    public FakeStatsService(Dictionary<Guid, PlayerStats> stats)
    //    {
    //        _stats = stats ?? new Dictionary<Guid, PlayerStats>();
    //    }

    //    public Task<List<PlayerStats>> EnrichPlayersAsync(List<PlayerRequestDto> players, CancellationToken cancellationToken = default)
    //    {
    //        var list = players.Select(p =>
    //        {
    //            if (_stats.TryGetValue(p.Id, out var s))
    //                return s;

    //            return new PlayerStats
    //            {
    //                PlayerId = p.Id,
    //                Name = p.Name,
    //                Wins = 0,
    //                Ties = 0,
    //                Losses = 0,
    //                WinRate = 0.0,
    //                SynergyWith = new Dictionary<Guid, double>()
    //            };
    //        }).ToList();

    //        return Task.FromResult(list);
    //    }

    //    public Task<PlayerVisualStatsReport> GetVisualReportAsync(Guid groupId, CancellationToken cancellationToken = default)
    //        => Task.FromResult(new PlayerVisualStatsReport
    //        {
    //            GroupId = groupId,
    //            TotalMatchesConsidered = 0,
    //            TotalFinalizedMatches = 0,
    //            TotalMatchesWithScore = 0,
    //            Players = new List<PlayerVisualStatsItem>()
    //        });
    //}
}