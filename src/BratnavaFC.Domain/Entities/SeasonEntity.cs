namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Temporada da patota: um período de datas [StartsAt, EndsAt). A partida pertence à
/// temporada pela data do jogo (PlayedAt), sem chave na partida. Os períodos da patota
/// ficam encostados, sem buraco nem sobreposição, e só uma fica aberta (EndsAt nulo).
/// Docs: docs/temporadas/TEMPORADAS.md.
/// </summary>
public sealed class SeasonEntity : BaseEntity
{
    public const int NameMaxLength = 40;

    public Guid GroupId { get; private set; }

    /// <summary>Ano a que a temporada pertence: o ano (Brasília) em que ela começa.</summary>
    public int Year { get; private set; }

    /// <summary>Ordem dentro do ano (2026/1, 2026/2...).</summary>
    public int Number { get; private set; }

    /// <summary>Nome dado pelo admin; nulo usa o padrão (2026 ou 2026/1).</summary>
    public string? Name { get; private set; }

    public DateTime StartsAt { get; private set; }
    public DateTime? EndsAt { get; private set; }

    /// <summary>Pré-temporada: conta na carreira, mas sai dos filtros e títulos do ano.</summary>
    public bool IsPreSeason { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    /// <summary>Admin que fechou; nulo quando a virada foi automática (ano novo).</summary>
    public Guid? ClosedByUserId { get; private set; }

    public bool IsOpen => EndsAt is null;

    // EF
    private SeasonEntity() { }

    public SeasonEntity(Guid groupId, int year, int number, DateTime startsAt, string? name = null, bool isPreSeason = false)
    {
        if (number < 1) throw new InvalidOperationException("Numero da temporada invalido.");
        GroupId = groupId;
        Year = year;
        Number = number;
        StartsAt = Utc(startsAt);
        Name = NormalizeName(name);
        IsPreSeason = isPreSeason;
    }

    public bool Contains(DateTime instant)
    {
        var utc = Utc(instant);
        return utc >= StartsAt && (EndsAt is null || utc < EndsAt);
    }

    public bool Contains(DateTimeOffset instant) => Contains(instant.UtcDateTime);

    public void Rename(string? name) => Name = NormalizeName(name);

    public void SetPreSeason(bool isPreSeason) => IsPreSeason = isPreSeason;

    public void Close(DateTime endsAt, Guid? closedByUserId, DateTime closedAt)
    {
        if (!IsOpen) throw new InvalidOperationException("Essa temporada ja esta fechada.");
        var end = Utc(endsAt);
        if (end <= StartsAt) throw new InvalidOperationException("O corte tem de ser depois do inicio da temporada.");
        EndsAt = end;
        ClosedAt = Utc(closedAt);
        ClosedByUserId = closedByUserId;
    }

    /// <summary>Desfaz o fechamento: volta a ser a temporada aberta.</summary>
    public void Reopen()
    {
        EndsAt = null;
        ClosedAt = null;
        ClosedByUserId = null;
    }

    /// <summary>Move o início (ajuste do corte com a temporada anterior).</summary>
    public void MoveStart(DateTime startsAt) => StartsAt = Utc(startsAt);

    /// <summary>Move o fim (ajuste do corte com a temporada seguinte).</summary>
    public void MoveEnd(DateTime endsAt)
    {
        if (IsOpen) throw new InvalidOperationException("A temporada aberta nao tem fim.");
        EndsAt = Utc(endsAt);
    }

    /// <summary>Nome exibido: o do admin; senão "2026" se o ano tiver uma temporada só, ou "2026/1".</summary>
    public string DisplayName(int seasonsInYear) =>
        Name ?? (seasonsInYear <= 1 ? $"{Year}" : $"{Year}/{Number}");

    private static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > NameMaxLength)
            throw new InvalidOperationException($"O nome da temporada pode ter no maximo {NameMaxLength} caracteres.");
        return trimmed;
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
