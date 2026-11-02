namespace BratnavaFC.Application.Abstractions;

public interface IConquistaProjectionService
{
    Task EnsureGroupProjectedAsync(Guid groupId, CancellationToken ct = default);
    Task ProjectMatchAsync(Guid groupId, Guid matchId, CancellationToken ct = default);
    Task RebuildGroupAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Sela os títulos das temporadas regulares já encerradas (e dos anos completos com
    /// 2+ temporadas regulares) que ainda não têm título. Devolve as temporadas seladas agora.
    /// </summary>
    Task<IReadOnlyList<Guid>> SealTitlesAsync(Guid groupId, DateTime? now = null, CancellationToken ct = default);

    /// <summary>Refaz os títulos desses anos (o admin mexeu nas temporadas deles).</summary>
    Task<IReadOnlyList<Guid>> ResealYearsAsync(Guid groupId, IEnumerable<int> years, DateTime? now = null, CancellationToken ct = default);
}
