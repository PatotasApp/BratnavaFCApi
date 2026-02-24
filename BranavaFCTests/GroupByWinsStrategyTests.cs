using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies; // <- ajuste namespace se necessário
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Xunit;

namespace BranavaFC.Tests;

public class GroupByWinsStrategyTests
{
    //[Fact]
    //public async Task GroupByWins_Returns_3Options_And_Alternates_TopByEffectiveWinRate()
    //{
    //    // Arrange
    //    var players = NewPlayersDeterministic(
    //        ("P1", false),
    //        ("P2", false),
    //        ("P3", false),
    //        ("P4", false)
    //    );

    //    var id = players.ToDictionary(p => p.Name, p => p.Id);

    //    // Effective winrate será o critério.
    //    // Todos com matches >= 3 para não cair no neutro.
    //    var stats = new Dictionary<Guid, PlayerStats>
    //    {
    //        // wr ordenado: P1(0.70), P2(0.66), P3(0.62), P4(0.58)
    //        [id["P1"]] = PS(id["P1"], "P1", wins: 7, ties: 0, losses: 3, winRate: 0.70),
    //        [id["P2"]] = PS(id["P2"], "P2", wins: 6, ties: 0, losses: 3, winRate: 0.66),
    //        [id["P3"]] = PS(id["P3"], "P3", wins: 5, ties: 0, losses: 3, winRate: 0.62),
    //        [id["P4"]] = PS(id["P4"], "P4", wins: 4, ties: 0, losses: 3, winRate: 0.58),
    //    };

    //    var fake = new FakeStatsService(stats);
    //    var strategy = new GroupByWinsStrategy(fake);

    //    var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = true };

    //    // Act
    //    var result = await strategy.GenerateTeamsAsync(players, settings);

    //    // Assert: novo contrato => 3 opções
    //    Assert.NotNull(result);
    //    Assert.NotNull(result.Options);
    //    Assert.Equal(3, result.Options.Count);

    //    // vamos validar a opção 0 (a primeira), porque GroupByWins normalmente é determinístico
    //    var opt = result.Options[0];

    //    Assert.Equal(2, opt.TeamA.Count);
    //    Assert.Equal(2, opt.TeamB.Count);

    //    // Regra pedida: "jogador com maior wins (aqui: maior effective winrate) vai alternado"
    //    // Ordenado: P1, P2, P3, P4
    //    // Alternando topo: P1 -> A, P2 -> B, P3 -> A, P4 -> B
    //    var teamAIds = opt.TeamA.Select(x => x.PlayerId).ToHashSet();
    //    var teamBIds = opt.TeamB.Select(x => x.PlayerId).ToHashSet();

    //    Assert.Contains(id["P1"], teamAIds);
    //    Assert.Contains(id["P3"], teamAIds);

    //    Assert.Contains(id["P2"], teamBIds);
    //    Assert.Contains(id["P4"], teamBIds);

    //    // sem duplicação
    //    Assert.Empty(teamAIds.Intersect(teamBIds));

    //    // Deve expor peso por jogador (front vai mostrar)
    //    Assert.All(opt.TeamA, p => Assert.True(p.Weight > 0, $"Weight inválido TeamA: {p.PlayerId} ({p.Weight})"));
    //    Assert.All(opt.TeamB, p => Assert.True(p.Weight > 0, $"Weight inválido TeamB: {p.PlayerId} ({p.Weight})"));

    //    // E deve expor effective winrate
    //    var p1pick = opt.TeamA.Concat(opt.TeamB).First(x => x.PlayerId == id["P1"]);
    //}

    //[Fact]
    //public async Task GroupByWins_Uses_Neutral_EffectiveWinRate_When_PlayerHasLessThan3Matches()
    //{
    //    // Arrange
    //    var players = NewPlayersDeterministic(("P1", false), ("P2", false), ("P3", false), ("P4", false));
    //    var id = players.ToDictionary(p => p.Name, p => p.Id);

    //    // P1 tem 2 matches (wins=2) -> deveria virar effective=0.50 (neutro)
    //    var stats = new Dictionary<Guid, PlayerStats>
    //    {
    //        [id["P1"]] = PS(id["P1"], "P1", wins: 2, ties: 0, losses: 0, winRate: 1.00),
    //        [id["P2"]] = PS(id["P2"], "P2", wins: 4, ties: 0, losses: 3, winRate: 0.57),
    //        [id["P3"]] = PS(id["P3"], "P3", wins: 4, ties: 0, losses: 4, winRate: 0.50),
    //        [id["P4"]] = PS(id["P4"], "P4", wins: 3, ties: 0, losses: 4, winRate: 0.43),
    //    };

    //    var fake = new FakeStatsService(stats);
    //    var strategy = new GroupByWinsStrategy(fake);

    //    var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = true };

    //    // Act
    //    var result = await strategy.GenerateTeamsAsync(players, settings);

    //    // Assert
    //    var opt = result.Options[0];
    //    var p1pick = opt.TeamA.Concat(opt.TeamB).First(x => x.PlayerId == id["P1"]);
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