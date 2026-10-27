using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Campeonato do dia: agrupa vários jogos (MatchEntity com ChampionshipId).
/// Os jogos ficam fora das consultas comuns de partidas por filtro global,
/// para não contarem nas estatísticas sem respeitar as regras do campeonato.
/// </summary>
public class ChampionshipEntity : BaseEntity
{
    private ChampionshipEntity() { } // EF

    public ChampionshipEntity(Guid groupId, string name, DateTime playedAt, string placeName)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId e obrigatorio.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nome do campeonato e obrigatorio.");
        if (string.IsNullOrWhiteSpace(placeName)) throw new InvalidOperationException("Local do campeonato e obrigatorio.");

        GroupId = groupId;
        Name = name.Trim();
        PlayedAt = playedAt;
        PlaceName = placeName.Trim();
        Status = ChampionshipStatus.Created;
    }

    public Guid GroupId { get; private set; }
    public GroupEntity? Group { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public DateTime PlayedAt { get; private set; }
    public string PlaceName { get; private set; } = string.Empty;

    public ChampionshipStatus Status { get; private set; }

    public List<MatchEntity> Matches { get; private set; } = [];
}
