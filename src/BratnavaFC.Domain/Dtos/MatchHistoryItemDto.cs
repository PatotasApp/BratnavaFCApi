using System;
using System.Text.Json.Serialization;

namespace BratnavaFC.Domain.Dtos;

public sealed record MatchHistoryItemDto(
    Guid Id,
    DateTime PlayedAt,
    int TeamAGoals,
    int TeamBGoals,
    int Status,
    string StatusName,
    string? PlaceName,
    string? TeamAColorHex,
    string? TeamBColorHex,
    IReadOnlyList<Guid> PlayerIds,
    Guid? LinkedPollId = null,
    string? LinkedPollTitle = null,
    string? LinkedPollType = null,

    // Modo Campeonato (Fase 3). Só vêm quando a lista inclui campeonatos (includeChampionships=true);
    // partidas comuns não ganham esses campos no JSON.
    // Kind: "championship" (consolidado: 1 card) ou "championshipGame" (jogo que conta como partida).
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ChampionshipId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChampionshipName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChampionTeamName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChampionTeamColorHex = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RuleBadges = null,
    // Pódio do campeonato consolidado (até 3º), no lugar do placar.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ChampionshipPodiumItemDto>? Podium = null
);

public sealed record ChampionshipPodiumItemDto(int Position, string TeamName, string? ColorHex);