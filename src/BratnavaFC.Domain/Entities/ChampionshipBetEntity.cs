using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Aposta de um usuário num campeonato. Usa o mesmo saldo de fichas das partidas
/// (UserBetBalanceEntity). Abre com os times definidos e fecha quando o campeonato inicia.
/// </summary>
public sealed class ChampionshipBetEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public Guid ChampionshipId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsResolved { get; private set; }

    private readonly List<ChampionshipBetSelectionEntity> _selections = new();
    public IReadOnlyList<ChampionshipBetSelectionEntity> Selections => _selections.AsReadOnly();

    public ChampionshipBetEntity(Guid groupId, Guid championshipId, Guid userId)
    {
        GroupId = groupId;
        ChampionshipId = championshipId;
        UserId = userId;
    }

    public void ReplaceSelections(IEnumerable<ChampionshipBetSelectionEntity> selections)
    {
        _selections.Clear();
        _selections.AddRange(selections);
        UpdateDate = DateTime.UtcNow;
    }

    public void MarkResolved()
    {
        IsResolved = true;
        UpdateDate = DateTime.UtcNow;
    }

    public void Unresolve()
    {
        IsResolved = false;
        foreach (var selection in _selections) selection.Unresolve();
        UpdateDate = DateTime.UtcNow;
    }

    // EF Core
    private ChampionshipBetEntity() { }
}

/// <summary>
/// Uma categoria apostada. PredictedId é o time (Champion) ou o jogador (demais categorias).
/// ActualIds: vencedores da categoria separados por vírgula (empate = vários; nenhum = vazio).
/// </summary>
public sealed class ChampionshipBetSelectionEntity : BaseEntity
{
    public Guid BetId { get; private set; }
    public ChampionshipBetEntity? Bet { get; private set; }
    public ChampionshipBetCategory Category { get; private set; }
    public Guid PredictedId { get; private set; }
    public int FichasWagered { get; private set; }

    public string? ActualIds { get; private set; }
    public int? FichasEarned { get; private set; }
    public bool? IsCorrect { get; private set; }

    public ChampionshipBetSelectionEntity(Guid betId, ChampionshipBetCategory category, Guid predictedId, int fichasWagered)
    {
        BetId = betId;
        Category = category;
        PredictedId = predictedId;
        FichasWagered = fichasWagered;
    }

    public void Resolve(IReadOnlyCollection<Guid> winners, int fichasEarned)
    {
        ActualIds = string.Join(",", winners);
        IsCorrect = winners.Contains(PredictedId);
        FichasEarned = fichasEarned;
        UpdateDate = DateTime.UtcNow;
    }

    public void Unresolve()
    {
        ActualIds = null;
        IsCorrect = null;
        FichasEarned = null;
        UpdateDate = DateTime.UtcNow;
    }

    // EF Core
    private ChampionshipBetSelectionEntity() { }
}
