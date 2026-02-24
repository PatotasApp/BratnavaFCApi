using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;
using Xunit;

namespace BranavaFC.Tests;

public class RandomStrategyTests
{
    //private static List<PlayerRequestDto> CreatePlayers(int count)
    //{
    //    var list = new List<PlayerRequestDto>();
    //    for (int i = 0; i < count; i++)
    //    {
    //        list.Add(new PlayerRequestDto(Guid.NewGuid(), $"P{i + 1}", false));
    //    }
    //    return list;
    //}

    //[Fact]
    //public async Task RandomStrategy_Returns_3Options_Correct_Number_And_No_Duplicates_PerOption()
    //{
    //    // Arrange
    //    var players = CreatePlayers(6);
    //    var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = true };
    //    var strategy = new RandomStrategy();

    //    // Act
    //    var result = await strategy.GenerateTeamsAsync(players, settings);

    //    // Assert: novo retorno -> 3 opções
    //    Assert.NotNull(result);
    //    Assert.NotNull(result.Options);
    //    Assert.Equal(3, result.Options.Count);

    //    foreach (var opt in result.Options)
    //    {
    //        Assert.NotNull(opt.TeamA);
    //        Assert.NotNull(opt.TeamB);

    //        Assert.Equal(3, opt.TeamA.Count);
    //        Assert.Equal(3, opt.TeamB.Count);

    //        // Sem duplicação entre A e B
    //        var aIds = opt.TeamA.Select(p => p.PlayerId).ToList();
    //        var bIds = opt.TeamB.Select(p => p.PlayerId).ToList();

    //        Assert.Equal(aIds.Count, aIds.Distinct().Count());
    //        Assert.Equal(bIds.Count, bIds.Distinct().Count());
    //        Assert.Empty(aIds.Intersect(bIds));

    //        // Total usado = 6, então Unassigned vazio
    //        var allPicked = aIds.Concat(bIds).ToList();
    //        Assert.Equal(players.Count, allPicked.Count);

    //        // Todos os players originais precisam estar em A ou B (nessa opção)
    //        var originalIds = players.Select(p => p.Id).OrderBy(x => x).ToList();
    //        var pickedSorted = allPicked.OrderBy(x => x).ToList();
    //        Assert.Equal(originalIds, pickedSorted);

    //        // Pesos por jogador precisam existir (front vai mostrar)
    //        Assert.All(opt.TeamA, p => Assert.True(p.Weight > 0, $"Weight inválido TeamA: {p.PlayerId} ({p.Weight})"));
    //        Assert.All(opt.TeamB, p => Assert.True(p.Weight > 0, $"Weight inválido TeamB: {p.PlayerId} ({p.Weight})"));

    //        // Unassigned vazio nessa opção (6 players = 3+3)
    //        Assert.True(opt.Unassigned is null || opt.Unassigned.Count == 0);
    //    }
    //}

    //[Fact]
    //public async Task RandomStrategy_Respects_Exclude_Goalkeepers()
    //{
    //    // Arrange
    //    var gkId = Guid.NewGuid();

    //    var players = new List<PlayerRequestDto>
    //    {
    //        new PlayerRequestDto(gkId, "Gk", true),
    //        new PlayerRequestDto(Guid.NewGuid(), "P1", false),
    //        new PlayerRequestDto(Guid.NewGuid(), "P2", false),
    //        new PlayerRequestDto(Guid.NewGuid(), "P3", false),
    //    };

    //    var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
    //    var strategy = new RandomStrategy();

    //    // Act
    //    var result = await strategy.GenerateTeamsAsync(players, settings);

    //    // Assert
    //    Assert.NotNull(result);
    //    Assert.NotNull(result.Options);
    //    Assert.Equal(3, result.Options.Count);

    //    foreach (var opt in result.Options)
    //    {
    //        var aIds = opt.TeamA.Select(p => p.PlayerId).ToList();
    //        var bIds = opt.TeamB.Select(p => p.PlayerId).ToList();
    //        var picked = aIds.Concat(bIds).ToHashSet();

    //        // GK não pode entrar
    //        Assert.DoesNotContain(gkId, aIds);
    //        Assert.DoesNotContain(gkId, bIds);

    //        // GK deve aparecer no unassigned (por opção)
    //        Assert.NotNull(opt.Unassigned);
    //        Assert.Contains(gkId, opt.Unassigned);

    //        // Os times devem ser preenchidos só com linha (total linha=3)
    //        // perTeam=2 => slots=4, mas só existem 3 jogadores de linha.
    //        // Logo, deve ter no máximo 3 picks no total.
    //        Assert.True(picked.Count <= 3);

    //        // Sem duplicar player
    //        Assert.Equal(picked.Count, aIds.Count + bIds.Count); // se houve duplicata, isso quebra

    //        // Pesos existem nos picks
    //        Assert.All(opt.TeamA, p => Assert.True(p.Weight > 0));
    //        Assert.All(opt.TeamB, p => Assert.True(p.Weight > 0));
    //    }
    //}
}