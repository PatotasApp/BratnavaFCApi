namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Título carimbado ao fechar a temporada (ou o ano); não muda quando uma partida é
/// corrigida depois. Só é refeito quando o admin mexe na temporada (fechar, desfazer,
/// ajustar o corte, marcar pré-temporada).
/// </summary>
public sealed class SeasonTitleEntity : BaseEntity
{
    public const short ScopeSeason = 1;
    public const short ScopeYear = 2;

    public Guid GroupId { get; private set; }
    public Guid PlayerId { get; private set; }

    /// <summary>Ano do título (o ano da temporada, ou o ano inteiro).</summary>
    public int Season { get; private set; }

    /// <summary>Temporada do título; nulo no título do ano.</summary>
    public Guid? SeasonId { get; private set; }

    /// <summary><see cref="ScopeSeason"/> ou <see cref="ScopeYear"/>.</summary>
    public short Scope { get; private set; } = ScopeSeason;

    public string Category { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Icon { get; private set; } = "";
    public int Position { get; private set; }
    public int Value { get; private set; }
    public DateTimeOffset AwardedAt { get; private set; }

    private SeasonTitleEntity() { }

    public SeasonTitleEntity(Guid groupId, Guid playerId, int season,
        string category, string name, string icon, int position, int value,
        Guid? seasonId = null, short scope = ScopeSeason)
    {
        GroupId = groupId;
        PlayerId = playerId;
        Season = season;
        SeasonId = seasonId;
        Scope = scope;
        Category = category;
        Name = name;
        Icon = icon;
        Position = position;
        Value = value;
        AwardedAt = DateTimeOffset.UtcNow;
    }
}
