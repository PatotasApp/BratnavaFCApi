namespace BratnavaFC.Domain.Dtos;

// ── Request ──────────────────────────────────────────────────────────────────

public record BetSelectionRequestDto(
    string Category,       // "WinningTeam" | "FinalScore" | "PlayerGoals" | "PlayerAssists"
    string PredictedValue, // See MatchBetSelectionEntity for formats
    int    FichasWagered
);

public record PlaceMatchBetDto(
    List<BetSelectionRequestDto> Selections
);

// ── Response ─────────────────────────────────────────────────────────────────

public record BetSelectionDto(
    Guid    Id,
    string  Category,
    string  PredictedValue,
    string? ActualValue,     // null até a partida ser resolvida
    int     FichasWagered,
    int?    FichasEarned,
    bool?   IsCorrect,
    bool?   IsPartialCredit
);

public record MatchBetDto(
    Guid                  Id,
    Guid                  MatchId,
    Guid                  UserId,
    string                UserName,
    bool                  IsResolved,
    bool                  IsLocked,
    List<BetSelectionDto> Selections,
    int?                  TotalFichasEarned
);

public record BetPlayerDto(
    Guid   MatchPlayerId,
    Guid   PlayerId,
    string Name,
    short  Team,
    bool   IsGuest,
    bool   HasBet,
    int?   TotalFichasWagered  // null se ainda não apostou
);

/// <summary>Dados da partida atual para montar o formulário de aposta.</summary>
public record CurrentMatchBetContextDto(
    Guid               MatchId,
    DateTime           PlayedAt,
    string             StatusName,
    bool               BetWindowOpen,  // status == MatchMaking
    List<BetPlayerDto> Players,
    MatchBetDto?       MyBet           // null se ainda não apostou
);

// ── Resultados de uma partida ─────────────────────────────────────────────────

public record UserBetResultDto(
    Guid                  UserId,
    string                UserName,
    List<BetSelectionDto> Selections,
    int                   TotalFichasEarned, // inclui +200 base
    int                   CurrentBalance
);

public record MatchBetResultsDto(
    Guid                   MatchId,
    bool                   IsResolved,
    List<UserBetResultDto> UserBets
);

// ── Histórico ─────────────────────────────────────────────────────────────────

public record UserBetInHistoryDto(
    Guid                  UserId,
    string                UserName,
    DateTime              PlacedAt,
    List<BetSelectionDto> Selections,
    int                   BaseReward,    // +200 sempre
    int                   BetEarnings,   // soma dos FichasEarned (pode ser negativo)
    int                   TotalForMatch  // BaseReward + BetEarnings
);

public record MatchBetHistoryDto(
    Guid                      MatchId,
    DateTime                  PlayedAt,
    int                       TeamAGoals,
    int                       TeamBGoals,
    List<UserBetInHistoryDto>  UserBets
);

// ── Leaderboard ───────────────────────────────────────────────────────────────

public record BetLeaderboardEntryDto(
    int    Rank,
    Guid   UserId,
    string UserName,
    int    Balance,
    int    TotalBets,
    int    TotalCorrect
);
