using System;

namespace BratnavaFC.Domain.Dtos;

public sealed class UpsertGroupSettingsDto
{
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string? DefaultPlaceName { get; set; }
    public DayOfWeek? DefaultDayOfWeek { get; set; }
    public TimeSpan? DefaultKickoffTime { get; set; }

    // ── Ícones configuráveis ─────────────────────────────────────────────────
    public string? GoalIcon       { get; set; }
    public string? GoalkeeperIcon { get; set; }
    public string? AssistIcon     { get; set; }
    public string? OwnGoalIcon    { get; set; }
    public string? MvpIcon        { get; set; }
    public string? PlayerIcon     { get; set; }
    public string? Rank1Icon      { get; set; }
    public string? Rank2Icon      { get; set; }
    public string? Rank3Icon      { get; set; }

    // ── Pagamento ────────────────────────────────────────────────────────────
    /// <summary>0 = Monthly, 1 = PerGame. Null = não alterar.</summary>
    public int?     PaymentMode          { get; set; }
    public decimal? MonthlyFee           { get; set; }
    public decimal? GoalkeeperMonthlyFee { get; set; }

    // ── Regra de empate no MVP ────────────────────────────────────────────────
    /// <summary>0 = NoMvp, 1 = AllMvp, 2 = AllMvpUpToMax. Null = não alterar.</summary>
    public int? MvpTieRule       { get; set; }
    public int? MvpTieMaxPlayers { get; set; }

    // ── Visibilidade de estatísticas ──────────────────────────────────────────
    /// <summary>null = não alterar</summary>
    public bool? ShowPlayerStats { get; set; }

    // ── Notificações configuráveis ────────────────────────────────────────────
    /// <summary>Dia do mês (1–28) para vencimento da mensalidade. Null = sem lembrete.</summary>
    public int? PaymentDueDay        { get; set; }
    /// <summary>Horas após encerrar para finalizar MVP automaticamente. Null = desativado.</summary>
    public int? AutoFinalizeMvpHours { get; set; }
}
