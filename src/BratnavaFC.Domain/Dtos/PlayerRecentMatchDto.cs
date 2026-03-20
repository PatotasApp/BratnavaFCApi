using System;

namespace BratnavaFC.Domain.Dtos;

/// <summary>
/// Resumo rico de uma partida para o dashboard do jogador.
/// Retornado em lista pelo endpoint GET /api/Matches/group/{groupId}/player-recent.
/// Elimina a necessidade de N chamadas extras ao endpoint de detalhes.
/// </summary>
public sealed record PlayerRecentMatchDto(
    Guid   MatchId,
    DateTime PlayedAt,
    int    TeamAGoals,
    int    TeamBGoals,
    string StatusName,
    string? PlaceName,

    // Cores dos times
    string? TeamAColorHex,
    string? TeamAColorName,
    string? TeamBColorHex,
    string? TeamBColorName,

    // Dados específicos do jogador
    int  PlayerTeam,     // 1 = Time A, 2 = Time B
    int  PlayerGoals,
    int  PlayerAssists,
    bool IsPlayerMvp
);
