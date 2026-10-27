namespace BratnavaFC.Domain.Entities;

/// <summary>Time fixo do campeonato. Cada jogo (MatchEntity) aponta para dois deles.</summary>
public class ChampionshipTeamEntity : BaseEntity
{
    private ChampionshipTeamEntity() { } // EF

    public ChampionshipTeamEntity(Guid championshipId, string name, Guid? colorId, int order)
    {
        if (championshipId == Guid.Empty) throw new InvalidOperationException("ChampionshipId e obrigatorio.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nome do time e obrigatorio.");

        ChampionshipId = championshipId;
        Name = name.Trim();
        ColorId = colorId;
        Order = order;
    }

    public Guid ChampionshipId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid? ColorId { get; private set; }
    public TeamColorEntity? Color { get; private set; }

    /// <summary>Ordem do time: define a fila inicial do fica quem ganha e os confrontos do mata-mata.</summary>
    public int Order { get; private set; }

    /// <summary>Posição final, gravada ao finalizar o campeonato. Empatados dividem a posição.</summary>
    public int? FinalPosition { get; private set; }

    internal void SetFinalPosition(int? position) => FinalPosition = position;
}
