using System;

namespace BratnavaFC.Domain.Dtos;

public sealed class GroupSettingsDto
{
    public Guid GroupId { get; init; }

    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; }

    public string? DefaultPlaceName { get; init; }

    public DayOfWeek? DefaultDayOfWeek { get; init; }
    public TimeSpan? DefaultKickoffTime { get; init; }

    public bool IsPersisted { get; init; }

    // ── Ícones configuráveis ─────────────────────────────────────────────────
    public string? GoalIcon       { get; init; }
    public string? GoalkeeperIcon { get; init; }
    public string? AssistIcon     { get; init; }
    public string? OwnGoalIcon    { get; init; }
    public string? MvpIcon        { get; init; }
    public string? PlayerIcon     { get; init; }
    public string? Rank1Icon      { get; init; }
    public string? Rank2Icon      { get; init; }
    public string? Rank3Icon      { get; init; }

    // ── Pagamento ────────────────────────────────────────────────────────────
    /// <summary>0 = Monthly, 1 = PerGame</summary>
    public int      PaymentMode { get; init; }
    public decimal? MonthlyFee  { get; init; }

    // ── Regra de empate no MVP ────────────────────────────────────────────────
    /// <summary>0 = NoMvp, 1 = AllMvp, 2 = AllMvpUpToMax</summary>
    public int MvpTieRule       { get; init; }
    public int MvpTieMaxPlayers { get; init; }

    // ── Visibilidade de estatísticas ──────────────────────────────────────────
    public bool ShowPlayerStats { get; init; }
}
