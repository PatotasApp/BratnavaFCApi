namespace BratnavaFC.Domain.Common;

/// <summary>
/// Fuso horário do Brasil (Brasília) resolvido uma única vez, com fallback UTC-3 fixo.
/// Use <see cref="Instance"/> com <c>TimeZoneInfo.ConvertTimeToUtc</c> /
/// <c>ConvertTimeFromUtc</c> ao converter datas locais ↔ UTC.
/// </summary>
public static class BrazilTimeZone
{
    public static TimeZoneInfo Instance { get; } = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        // Fallback: UTC-3 fixo (sem horário de verão)
        return TimeZoneInfo.CreateCustomTimeZone(
            "BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }
}
