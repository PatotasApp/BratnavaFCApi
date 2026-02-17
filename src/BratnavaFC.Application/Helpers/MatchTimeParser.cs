internal static class MatchTimeParser
{
    public static int? ParseToSeconds(string? mmss)
    {
        if (string.IsNullOrWhiteSpace(mmss)) return null;

        var parts = mmss.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new InvalidOperationException("Tempo invalido. Use MM:SS (ex: 12:34).");

        if (!int.TryParse(parts[0], out var minutes) || minutes < 0)
            throw new InvalidOperationException("Minutos invalidos no tempo do gol.");

        if (!int.TryParse(parts[1], out var seconds) || seconds < 0 || seconds > 59)
            throw new InvalidOperationException("Segundos invalidos no tempo do gol (0-59).");

        checked { return minutes * 60 + seconds; }
    }

    public static string? FormatFromSeconds(int? totalSeconds)
    {
        if (!totalSeconds.HasValue) return null;
        if (totalSeconds.Value < 0) return null;

        var m = totalSeconds.Value / 60;
        var s = totalSeconds.Value % 60;
        return $"{m:00}:{s:00}";
    }
}
