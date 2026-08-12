namespace BratnavaFC.Domain.Entities;

/// <summary>Título carimbado ao encerrar a temporada; não muda em reconstruções posteriores.</summary>
public sealed class SeasonTitleEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public Guid PlayerId { get; private set; }
    public int Season { get; private set; }
    public string Category { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Icon { get; private set; } = "";
    public int Position { get; private set; }
    public int Value { get; private set; }
    public DateTimeOffset AwardedAt { get; private set; }

    private SeasonTitleEntity() { }

    public SeasonTitleEntity(Guid groupId, Guid playerId, int season,
        string category, string name, string icon, int position, int value)
    {
        GroupId = groupId;
        PlayerId = playerId;
        Season = season;
        Category = category;
        Name = name;
        Icon = icon;
        Position = position;
        Value = value;
        AwardedAt = DateTimeOffset.UtcNow;
    }
}
