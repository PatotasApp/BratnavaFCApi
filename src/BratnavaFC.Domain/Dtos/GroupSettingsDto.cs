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
    public int      PaymentMode          { get; init; }
    public decimal? MonthlyFee           { get; init; }
    public decimal? GoalkeeperMonthlyFee { get; init; }

    // ── Regra de empate no MVP ────────────────────────────────────────────────
    /// <summary>0 = NoMvp, 1 = AllMvp, 2 = AllMvpUpToMax</summary>
    public int MvpTieRule       { get; init; }
    public int MvpTieMaxPlayers { get; init; }

    // ── Visibilidade de estatísticas ──────────────────────────────────────────
    public bool ShowPlayerStats { get; init; }
    public bool ShowStatsGeneralTab { get; init; }
    public bool ShowStatsPerMatchTab { get; init; }
    public bool ShowStatsClassificationTab { get; init; }

    // ── Notificações configuráveis ────────────────────────────────────────────
    public int? PaymentDueDay        { get; init; }
    public int? AutoFinalizeMvpHours { get; init; }

    // ── Agendamento automático de partidas ───────────────────────────────────
    /// <summary>0 = Manual, 1 = Recurring</summary>
    public bool MatchSchedulingEnabled { get; init; }
    public int MatchSchedulingMode { get; init; }
    public DayOfWeek? MatchScheduleDayOfWeek { get; init; }
    public TimeSpan? MatchScheduleTime { get; init; }
    public List<ManualMatchScheduleDto> ManualMatchSchedules { get; init; } = new();
}

public sealed class ManualMatchScheduleDto
{
    public DateTime PlayedAt { get; init; }
    public bool Created { get; init; }
    public Guid? MatchId { get; init; }
}
