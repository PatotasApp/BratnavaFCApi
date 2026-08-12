namespace BratnavaFC.Application.Services;

public static class NotificationContentFormatter
{
    private static readonly TimeZoneInfo BrazilTimeZone = ResolveBrazilTimeZone();

    public static string MatchInviteBody(DateTime playedAt, string? placeName)
    {
        var utc = playedAt.Kind switch
        {
            DateTimeKind.Utc => playedAt,
            DateTimeKind.Local => playedAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(playedAt, DateTimeKind.Utc),
        };
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, BrazilTimeZone);
        var location = string.IsNullOrWhiteSpace(placeName)
            ? string.Empty
            : $" · {placeName.Trim()}";

        return $"{local:dd/MM/yyyy 'às' HH:mm}{location}. Confirme sua presença.";
    }

    public static string PollInviteBody(string title, bool isEvent)
    {
        var normalizedTitle = title.Trim();
        return isEvent
            ? $"Evento: {normalizedTitle}. Confirme se você vai participar."
            : $"Votação: {normalizedTitle}. Participe e registre seu voto.";
    }

    private static TimeZoneInfo ResolveBrazilTimeZone()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }
}
