using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Application.Statistics;

/// <summary>
/// Converte unidades de campeonato nos itens das listas de histórico. Os números vêm da
/// mesma unidade que alimenta as estatísticas, então histórico e estatística nunca divergem.
/// </summary>
public static class ChampionshipHistoryItems
{
    public static MatchHistoryItemDto ToHistoryItem(ChampionshipUnit unit)
    {
        var c = unit.Championship;
        var champion = c.Teams.FirstOrDefault(t => t.FinalPosition == 1);
        var badges = c.Rules.BuildBadges();
        var playerIds = unit.Stat.AllPlayers.Select(p => p.PlayerId).Distinct().ToList();

        if (unit.Game is { } game)
        {
            var (colorA, _) = TeamColor(unit, game.ChampionshipTeamAId);
            var (colorB, _) = TeamColor(unit, game.ChampionshipTeamBId);
            return new MatchHistoryItemDto(
                game.Id, game.PlayedAt, game.TeamAGoals ?? 0, game.TeamBGoals ?? 0,
                (int)MatchStatus.Finalized, MatchStatus.Finalized.ToString(), c.PlaceName, colorA, colorB, playerIds,
                Kind: "championshipGame", ChampionshipId: c.Id, ChampionshipName: c.Name,
                ChampionTeamName: champion?.Name, ChampionTeamColorHex: champion?.Color?.HexValue, RuleBadges: badges);
        }

        return new MatchHistoryItemDto(
            c.Id, c.PlayedAt, 0, 0,
            (int)MatchStatus.Finalized, MatchStatus.Finalized.ToString(), c.PlaceName, null, null, playerIds,
            Kind: "championship", ChampionshipId: c.Id, ChampionshipName: c.Name,
            ChampionTeamName: champion?.Name, ChampionTeamColorHex: champion?.Color?.HexValue, RuleBadges: badges,
            Podium: Podium(c));
    }

    /// <summary>Item da lista do jogador, ou null se ele não conta nessa unidade.</summary>
    public static PlayerRecentMatchDto? ToPlayerItem(ChampionshipUnit unit, Guid playerId)
    {
        var sideIndex = unit.Stat.Sides.ToList().FindIndex(s => s.Players.Any(p => p.PlayerId == playerId));
        if (sideIndex < 0) return null;

        var side = unit.Stat.Sides[sideIndex];
        var player = side.Players.First(p => p.PlayerId == playerId);
        var goals = unit.Stat.Goals;
        var c = unit.Championship;

        var playerGoals = goals.Count(g => !g.IsOwnGoal && g.ScorerPlayerId == playerId);
        var playerAssists = goals.Count(g => !g.IsOwnGoal && g.AssistPlayerId == playerId);
        var playerOwnGoals = goals.Count(g => g.IsOwnGoal && g.ScorerPlayerId == playerId);
        var result = player.Result switch
        {
            StatResult.Win => "W",
            StatResult.Draw => "D",
            StatResult.Loss => "L",
            _ => "N"
        };

        if (unit.Game is { } game)
        {
            var (hexA, nameA) = TeamColor(unit, game.ChampionshipTeamAId);
            var (hexB, nameB) = TeamColor(unit, game.ChampionshipTeamBId);
            return new PlayerRecentMatchDto(
                game.Id, game.PlayedAt, game.TeamAGoals ?? 0, game.TeamBGoals ?? 0,
                MatchStatus.Finalized.ToString(), c.PlaceName, hexA, nameA, hexB, nameB,
                sideIndex + 1, playerGoals, playerAssists, playerOwnGoals, player.IsMvp,
                Kind: "championshipGame", ChampionshipId: c.Id, ChampionshipName: c.Name, PlayerResult: result);
        }

        // Consolidado: o "time" do jogador é o time dele no campeonato; não há placar único.
        var team = c.Teams.FirstOrDefault(t => t.Id == side.TeamId);
        return new PlayerRecentMatchDto(
            c.Id, c.PlayedAt, 0, 0,
            MatchStatus.Finalized.ToString(), c.PlaceName, team?.Color?.HexValue, team?.Name, null, null,
            1, playerGoals, playerAssists, playerOwnGoals, player.IsMvp,
            Kind: "championship", ChampionshipId: c.Id, ChampionshipName: c.Name, PlayerResult: result,
            Podium: Podium(c));
    }

    /// <summary>Pódio do consolidado (1º a 3º), mostrado no lugar do placar.</summary>
    private static List<ChampionshipPodiumItemDto> Podium(ChampionshipEntity c) => c.Teams
        .Where(t => t.FinalPosition is >= 1 and <= 3)
        .OrderBy(t => t.FinalPosition).ThenBy(t => t.Order)
        .Select(t => new ChampionshipPodiumItemDto(t.FinalPosition!.Value, t.Name, t.Color?.HexValue))
        .ToList();

    private static (string? Hex, string? Name) TeamColor(ChampionshipUnit unit, Guid? teamId)
    {
        var team = unit.Championship.Teams.FirstOrDefault(t => t.Id == teamId);
        return (team?.Color?.HexValue, team?.Name);
    }
}
