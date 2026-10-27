using System.Text.Json;
using System.Text.Json.Nodes;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;

namespace BranavaFC.Tests;

/// <summary>
/// Trava o JSON do campeonato no formato de docs/campeonato/CONTRATO-API.md, que o app
/// Flutter usa ao pé da letra. Mudou um nome aqui? Mude o contrato e avise os clientes.
/// </summary>
public sealed class CampeonatoContratoTests
{
    // Mesmas opções do ASP.NET Core (camelCase, enums como número).
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Detalhe_TemAsChavesDoContrato()
    {
        var id = Guid.NewGuid();
        var dto = new ChampionshipDetailsDto(
            id, "Torneio", DateTime.UtcNow, "Arena", ChampionshipStatus.InProgress,
            ChampionshipEntity.DefaultRules, true, ["Conta como 1 partida"],
            [new ChampionshipParticipantDto(id, "Ana", null, false, true, InviteResponse.Accepted, id, false)],
            [new ChampionshipTeamDto(id, "Azul", null, null, null, [id])],
            [new ChampionshipGameDto(id, 1, id, id, 1, 0, null, ChampionshipGameStatus.Finished, null,
                [new ChampionshipGameGoalDto(id, id, null, false, null)])],
            [new ChampionshipStandingDto(1, id, 1, 1, 0, 0, 1, 0, 1, 3, 3m)],
            null, [], [new ChampionshipScorerDto(id, 1)], [new ChampionshipAssistDto(id, 1)],
            [], InviteResponse.Accepted, false, [new ChampionshipMyVoteDto(null, id)]);

        var json = JsonSerializer.SerializeToNode(dto, Web)!.AsObject();

        Keys(json).Should().BeEquivalentTo(
            "id", "name", "playedAt", "placeName", "status", "rules", "rulesLocked", "ruleBadges", "players",
            "teams", "games", "standings", "currentGameId", "waitingTeamIds", "topScorers", "topAssists",
            "mvpPlayerIds", "myInviteResponse", "canVote", "myVotes");
        Keys(First(json, "players")).Should().BeEquivalentTo(
            "playerId", "name", "photoUrl", "isGuest", "isGoalkeeper", "inviteResponse", "teamId", "didNotPlay");
        Keys(First(json, "teams")).Should().BeEquivalentTo("id", "name", "colorId", "colorHex", "finalPosition", "playerIds");
        Keys(First(json, "games")).Should().BeEquivalentTo(
            "id", "order", "teamAId", "teamBId", "teamAGoals", "teamBGoals", "externalWinnerTeamId", "status", "startedAt", "goals");
        Keys(First(First(json, "games"), "goals")).Should().BeEquivalentTo(
            "id", "scorerPlayerId", "assistPlayerId", "isOwnGoal", "timeSeconds");
        Keys(First(json, "standings")).Should().BeEquivalentTo(
            "position", "teamId", "played", "wins", "draws", "losses", "goalsFor", "goalsAgainst",
            "goalDifference", "points", "pointsPerGame");
        Keys(First(json, "topScorers")).Should().BeEquivalentTo("playerId", "goals");
        Keys(First(json, "topAssists")).Should().BeEquivalentTo("playerId", "assists");
        Keys(First(json, "myVotes")).Should().BeEquivalentTo("gameId", "votedPlayerId");
        json["status"]!.GetValueKind().Should().Be(JsonValueKind.Number, "enums trafegam como número");
    }

    [Fact]
    public void Regras_TemAsChavesDoContrato()
    {
        var json = JsonSerializer.SerializeToNode(ChampionshipEntity.DefaultRules, Web)!.AsObject();

        Keys(json).Should().BeEquivalentTo(
            "historyMode", "resultMode", "placementResults", "mvpMode", "mvpCountsInGeneralStats",
            "goalsCountInGeneralStats", "includeInTeamDraw", "minGamesForPlacementResult", "format",
            "roundRobinTurns", "winnerStays", "gameEnd", "standings");
        Keys(json["winnerStays"]!.AsObject()).Should().BeEquivalentTo("consecutiveWinsLimit", "tieRule", "limitRule");
        Keys(json["gameEnd"]!.AsObject()).Should().BeEquivalentTo("timeLimitMinutes", "goalLimit");
        Keys(json["standings"]!.AsObject()).Should().BeEquivalentTo("mode", "winPoints", "drawPoints", "tiebreakers");

        // O app manda as regras e a API tem de ler de volta exatamente o mesmo conteúdo.
        var roundTrip = JsonSerializer.Deserialize<BratnavaFC.Domain.Championships.ChampionshipRuleSet>(json.ToJsonString(), Web)!;
        JsonSerializer.Serialize(roundTrip, Web).Should().Be(JsonSerializer.Serialize(ChampionshipEntity.DefaultRules, Web));
    }

    [Fact]
    public void Resumo_TemAsChavesDoContrato()
    {
        var dto = new ChampionshipSummaryDto(Guid.NewGuid(), "Torneio", DateTime.UtcNow, "Arena",
            ChampionshipStatus.Finalized, 3, 5, "Azul", "#00F", ["Conta como 1 partida"]);

        Keys(JsonSerializer.SerializeToNode(dto, Web)!.AsObject()).Should().BeEquivalentTo(
            "id", "name", "playedAt", "placeName", "status", "teamCount", "gameCount",
            "championTeamName", "championTeamColorHex", "ruleBadges");
    }

    private static IEnumerable<string> Keys(JsonObject obj) => obj.Select(kv => kv.Key);

    private static JsonObject First(JsonObject obj, string arrayKey) => obj[arrayKey]!.AsArray()[0]!.AsObject();
}
