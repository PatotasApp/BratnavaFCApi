namespace BratnavaFC.Domain.Dtos.Seasons;

public sealed record SeasonDto(
    Guid Id,
    int Year,
    int Number,
    string? Name,
    string DisplayName,
    DateTime StartsAt,
    DateTime? EndsAt,
    bool IsPreSeason,
    bool IsOpen,
    bool IsCurrent,
    int Matches,
    int Championships,
    DateTime? ClosedAt,
    string? ClosedByName);

/// <summary>Temporadas da patota, da mais antiga para a mais nova.</summary>
public sealed record SeasonsOverviewDto(
    IReadOnlyList<SeasonDto> Seasons,
    Guid? CurrentSeasonId,
    /// <summary>Algum admin já fechou temporada: o ano novo não vira sozinho.</summary>
    bool ManualMode,
    /// <summary>Último fechamento feito pelo admin pode ser desfeito.</summary>
    bool CanUndoClose);

/// <summary>Resumo antes de fechar: o que fica em cada temporada com esse corte.</summary>
public sealed record SeasonClosePreviewDto(
    Guid ClosingSeasonId,
    string ClosingDisplayName,
    DateTime ClosingStartsAt,
    DateTime CutAt,
    int ClosingMatches,
    int ClosingChampionships,
    int NextYear,
    int NextNumber,
    string NextDefaultName,
    int MovedMatches,
    int MovedChampionships);

/// <summary>Fecha a temporada aberta e abre a próxima a partir do corte (padrão: agora).</summary>
public sealed record CloseSeasonDto(DateTimeOffset? CutAt, string? NextName, bool NextIsPreSeason);

public sealed record UpdateSeasonDto(string? Name, bool IsPreSeason);

/// <summary>Novo corte entre a temporada e a anterior.</summary>
public sealed record MoveSeasonStartDto(DateTimeOffset StartsAt);

/// <summary>
/// Retrospectiva de uma temporada encerrada (fase 4): totais, títulos e a classificação
/// real de cada categoria (posição de verdade, sem o "top x%" de durante a temporada).
/// </summary>
public sealed record SeasonSummaryDto(
    SeasonDto Season,
    int Matches,
    int Championships,
    int Goals,
    int Players,
    bool TitlesSealed,
    IReadOnlyList<SeasonRankingDto> Rankings);

public sealed record SeasonRankingDto(
    string Category,
    string Name,
    string Icon,
    /// <summary>Mínimo de jogos para entrar (0 = sem mínimo).</summary>
    int MinGames,
    IReadOnlyList<SeasonRankingRowDto> Rows);

public sealed record SeasonRankingRowDto(
    int Position,
    Guid PlayerId,
    string PlayerName,
    int Value,
    int Games,
    bool IsGuest);
