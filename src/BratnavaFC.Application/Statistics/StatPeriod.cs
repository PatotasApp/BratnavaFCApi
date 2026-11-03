namespace BratnavaFC.Application.Statistics;

/// <summary>
/// Período de estatística: uma temporada, um ano (com ou sem pré-temporadas) ou tudo
/// (<c>null</c> em quem recebe). O jogo entra pela data (PlayedAt, UTC).
/// </summary>
public sealed record StatPeriod(IReadOnlyList<StatPeriodRange> Ranges, string Label)
{
    public bool Contains(DateTime playedAt)
    {
        var utc = playedAt.Kind == DateTimeKind.Utc ? playedAt : DateTime.SpecifyKind(playedAt, DateTimeKind.Utc);
        return Ranges.Any(r => utc >= r.Start && (r.End is null || utc < r.End));
    }

    public bool Contains(DateTimeOffset playedAt) => Contains(playedAt.UtcDateTime);
}

public sealed record StatPeriodRange(DateTime Start, DateTime? End);
