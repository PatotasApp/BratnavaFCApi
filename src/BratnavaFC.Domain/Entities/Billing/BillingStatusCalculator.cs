using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities.Billing;

/// <summary>Situação calculada de uma patota num instante.</summary>
public sealed record GroupBillingSnapshot(
    GroupBillingStatus Status,
    DateTime AccessEndsAt,
    DateTime GraceEndsAt,
    /// <summary>Dias de calendário (horário de Brasília) até o vencimento; negativo depois de vencer.</summary>
    int DaysRemaining,
    /// <summary>Data do vencimento no calendário de Brasília (último dia com acesso).</summary>
    DateOnly AccessEndsOn,
    /// <summary>Verdadeiro só quando venceu, passou da tolerância E o interruptor está ligado.</summary>
    bool IsReadOnly);

/// <summary>
/// Regra pura da situação da patota. Sem banco, para ser testada isolada e usada igual
/// pelo filtro de bloqueio, pelo card do app, pelo painel e pelos lembretes.
/// </summary>
public static class BillingStatusCalculator
{
    /// <summary>Brasil sem horário de verão desde 2019: UTC-3 fixo, como o resto da API.</summary>
    public static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static DateOnly BrasiliaDate(DateTime utc) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(BrasiliaOffset));

    public static GroupBillingSnapshot Calculate(GroupBillingEntity billing, int graceDays, bool enforcementEnabled, DateTime now)
    {
        var accessEnds = billing.AccessEndsAt;
        var graceEnds = accessEnds.AddDays(Math.Max(0, graceDays));

        // O vencimento é um instante; para o usuário vale o dia: "vence em 06/12" quer dizer
        // que o acesso vai até o fim de 06/12. Um instante exatamente à meia-noite conta
        // como o dia anterior.
        var endsOn = BrasiliaDate(accessEnds.AddTicks(-1));
        var today = BrasiliaDate(now);
        var days = endsOn.DayNumber - today.DayNumber;

        if (billing.IsLifetime)
            return new GroupBillingSnapshot(GroupBillingStatus.Lifetime, accessEnds, graceEnds, int.MaxValue, DateOnly.MaxValue, false);

        GroupBillingStatus status;
        if (now < accessEnds)
        {
            var paidCovers = billing.PaidUntil is { } paid && paid > now;
            status = paidCovers ? GroupBillingStatus.Active : GroupBillingStatus.Trial;
        }
        else if (now < graceEnds)
            status = GroupBillingStatus.Grace;
        else
            status = GroupBillingStatus.Expired;

        return new GroupBillingSnapshot(
            status, accessEnds, graceEnds, days, endsOn,
            IsReadOnly: enforcementEnabled && status == GroupBillingStatus.Expired);
    }
}
