namespace BratnavaFC.Domain.Dtos;

/// <summary>
/// Ranking dos replays mais curtidos (visão do admin e modo telão).
/// Years: anos (pela data da partida) que têm replays curtidos, para o filtro de temporada.
/// GroupName vai na abertura do telão.
/// </summary>
public record ReplayRankingDto(
    string? GroupName,
    IReadOnlyList<int> Years,
    IReadOnlyList<RankedReplayDto> Items
);

/// <summary>
/// Replay do ranking. Rank segue a regra das estatísticas: empatados dividem a colocação e a
/// próxima pula (1, 1, 3). ClipTime é o horário real do lance ("HH:mm:ss") tirado do nome do
/// arquivo quando existe; MatchPlayedAt é a data da partida (ou do campeonato, para jogo de
/// campeonato). LikedBy vem na ordem das curtidas, com o nome curto usado nas estatísticas.
/// </summary>
public record RankedReplayDto(
    Guid Id,
    Guid MatchId,
    string ObjectKey,
    string VideoUrl,
    string EventType,
    DateTimeOffset RecordedAt,
    string? ClipTime,
    DateTime? MatchPlayedAt,
    int Rank,
    int LikeCount,
    IReadOnlyList<string> LikedBy,
    string? TeamAName,
    string? TeamAColorHex,
    string? TeamBName,
    string? TeamBColorHex,
    int? TeamAGoals,
    int? TeamBGoals,
    Guid? ChampionshipId = null,
    string? ChampionshipName = null,
    int? ChampionshipGameOrder = null
);
