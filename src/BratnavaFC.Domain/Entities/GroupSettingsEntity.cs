using System;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public sealed class GroupSettingsEntity : BaseEntity
{
    private GroupSettingsEntity() { } // EF


    public GroupSettingsEntity(
        Guid groupId,
        int minPlayers,
        int maxPlayers,
        string? defaultPlaceName,
        DayOfWeek? defaultDayOfWeek,
        TimeSpan? defaultKickoffTime)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId e obrigatorio.");

        GroupId = groupId;

        SetPlayerLimits(minPlayers, maxPlayers);
        SetDefaultPlaceName(defaultPlaceName);
        SetDefaultSchedule(defaultDayOfWeek, defaultKickoffTime);
    }

    public Guid GroupId { get; private set; }

    public int MinPlayers { get; private set; }
    public int MaxPlayers { get; private set; }

    public string? DefaultPlaceName { get; private set; }

    public DayOfWeek? DefaultDayOfWeek { get; private set; }
    public TimeSpan? DefaultKickoffTime { get; private set; }

    // ── Ícones configuráveis por patota ───────────────────────────────────────
    public string? GoalIcon       { get; private set; }
    public string? GoalkeeperIcon { get; private set; }
    public string? AssistIcon     { get; private set; }
    public string? OwnGoalIcon    { get; private set; }
    public string? MvpIcon        { get; private set; }
    public string? PlayerIcon     { get; private set; }
    public string? Rank1Icon      { get; private set; }
    public string? Rank2Icon      { get; private set; }
    public string? Rank3Icon      { get; private set; }

    // ── Pagamento ─────────────────────────────────────────────────────────────
    public PaymentMode PaymentMode           { get; private set; } = PaymentMode.Monthly;
    /// <summary>Mensalidade para jogadores de linha.</summary>
    public decimal?    MonthlyFee            { get; private set; }
    /// <summary>Mensalidade para goleiros. Se nulo, usa MonthlyFee.</summary>
    public decimal?    GoalkeeperMonthlyFee  { get; private set; }

    public void SetPaymentMode(PaymentMode mode) => PaymentMode = mode;

    public void SetMonthlyFee(decimal? value)
    {
        if (value.HasValue && value.Value < 0)
            throw new InvalidOperationException("MonthlyFee nao pode ser negativo.");
        MonthlyFee = value;
    }

    public void SetGoalkeeperMonthlyFee(decimal? value)
    {
        if (value.HasValue && value.Value < 0)
            throw new InvalidOperationException("GoalkeeperMonthlyFee nao pode ser negativo.");
        GoalkeeperMonthlyFee = value;
    }

    // ── Regra de empate no MVP ────────────────────────────────────────────────
    public MvpTieRule MvpTieRule        { get; private set; } = MvpTieRule.AllMvp;
    public int        MvpTieMaxPlayers  { get; private set; } = 2;

    public void SetMvpTieRule(MvpTieRule rule, int? maxPlayers = null)
    {
        if (rule == MvpTieRule.AllMvpUpToMax)
        {
            if (!maxPlayers.HasValue || maxPlayers.Value < 2)
                throw new InvalidOperationException("MvpTieMaxPlayers deve ser pelo menos 2 quando a regra for AllMvpUpToMax.");
            MvpTieMaxPlayers = maxPlayers.Value;
        }
        MvpTieRule = rule;
    }

    // ── Visibilidade de estatísticas para jogadores ───────────────────────────
    public bool ShowPlayerStats { get; private set; } = false;

    public void SetShowPlayerStats(bool value) => ShowPlayerStats = value;

    public void Update(
        int minPlayers,
        int maxPlayers,
        string? defaultPlaceName,
        DayOfWeek? defaultDayOfWeek,
        TimeSpan? defaultKickoffTime)
    {
        SetPlayerLimits(minPlayers, maxPlayers);
        SetDefaultPlaceName(defaultPlaceName);
        SetDefaultSchedule(defaultDayOfWeek, defaultKickoffTime);
    }

    public void SetIcons(
        string? goalIcon,
        string? goalkeeperIcon,
        string? assistIcon,
        string? ownGoalIcon,
        string? mvpIcon,
        string? playerIcon,
        string? rank1Icon,
        string? rank2Icon,
        string? rank3Icon)
    {
        GoalIcon       = goalIcon;
        GoalkeeperIcon = goalkeeperIcon;
        AssistIcon     = assistIcon;
        OwnGoalIcon    = ownGoalIcon;
        MvpIcon        = mvpIcon;
        PlayerIcon     = playerIcon;
        Rank1Icon      = rank1Icon;
        Rank2Icon      = rank2Icon;
        Rank3Icon      = rank3Icon;
    }

    private void SetPlayerLimits(int minPlayers, int maxPlayers)
    {
        if (minPlayers <= 0)
            throw new InvalidOperationException("MinPlayers deve ser maior que 0.");

        if (maxPlayers <= 0)
            throw new InvalidOperationException("MaxPlayers deve ser maior que 0.");

        if (minPlayers > maxPlayers)
            throw new InvalidOperationException("MinPlayers nao pode ser maior que MaxPlayers.");

        if (maxPlayers > 22)
            throw new InvalidOperationException("MaxPlayers nao pode ser maior que 22.");

        MinPlayers = minPlayers;
        MaxPlayers = maxPlayers;
    }

    private void SetDefaultPlaceName(string? placeName)
    {
        DefaultPlaceName = string.IsNullOrWhiteSpace(placeName) ? null : placeName.Trim();
    }

    private void SetDefaultSchedule(DayOfWeek? dayOfWeek, TimeSpan? kickoffTime)
    {
        if (kickoffTime.HasValue)
        {
            if (kickoffTime.Value < TimeSpan.Zero || kickoffTime.Value >= TimeSpan.FromDays(1))
                throw new InvalidOperationException("DefaultKickoffTime invalido.");
        }

        DefaultDayOfWeek = dayOfWeek;
        DefaultKickoffTime = kickoffTime;
    }
}
