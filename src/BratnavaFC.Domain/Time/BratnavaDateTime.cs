namespace BratnavaFC.Domain.Time;

public static class BratnavaDateTime
{
    public static readonly TimeZoneInfo SaoPauloTimeZone = ResolveSaoPauloTimeZone();

    public static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static DateTime SaoPauloLocalToUtc(DateTime local)
    {
        if (local.Kind == DateTimeKind.Utc)
            return local;

        if (local.Kind == DateTimeKind.Local)
            return local.ToUniversalTime();

        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            SaoPauloTimeZone);
    }

    public static DateTime UtcToSaoPauloLocal(DateTime utc)
    {
        var normalized = EnsureUtc(utc);
        return DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(normalized, SaoPauloTimeZone),
            DateTimeKind.Unspecified);
    }

    public static DateTime SaoPauloDateTimeToUtc(DateOnly date, TimeOnly? time = null)
        => SaoPauloLocalToUtc(date.ToDateTime(time ?? new TimeOnly(23, 59, 59)));

    public static DateOnly TodayInSaoPaulo()
        => DateOnly.FromDateTime(UtcToSaoPauloLocal(DateTime.UtcNow));

    private static TimeZoneInfo ResolveSaoPauloTimeZone()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "America/Sao_Paulo",
            TimeSpan.FromHours(-3),
            "Sao Paulo Time",
            "Sao Paulo Time");
    }
}
