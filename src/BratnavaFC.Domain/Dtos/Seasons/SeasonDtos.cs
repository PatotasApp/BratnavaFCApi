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
