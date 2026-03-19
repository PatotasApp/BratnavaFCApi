namespace BratnavaFC.Domain.Models;

public sealed class PlayerSpotlightItem
{
    public Guid   PlayerId    { get; init; }
    public string Name        { get; init; } = string.Empty;
    public bool   IsGoalkeeper { get; init; }
    public bool   IsGuest     { get; init; }

    public int    GamesPlayed { get; init; }
    public int    Wins        { get; init; }
    public int    Losses      { get; init; }
    public int    Ties        { get; init; }
    public double WinRate     { get; init; }
    public int    Goals       { get; init; }
    public int    Assists     { get; init; }
    public int    Mvps        { get; init; }

    /// <summary>Top 3 parceiros com maior win rate junto (mín. 3 partidas juntos).</summary>
    public List<SpotlightRelation> BestPartners  { get; init; } = new();

    /// <summary>Top 3 parceiros com menor win rate junto (mín. 3 partidas juntos).</summary>
    public List<SpotlightRelation> WorstPartners { get; init; } = new();

    /// <summary>Top 3 adversários que mais me vencem (maior win rate contra mim, mín. 2 partidas frente-a-frente).</summary>
    public List<SpotlightRelation> MostBeatenBy  { get; init; } = new();

    /// <summary>Top 3 adversários que eu mais venci (maior win rate meu contra eles, mín. 2 partidas frente-a-frente).</summary>
    public List<SpotlightRelation> LeastBeatenBy { get; init; } = new();

    /// <summary>Top 3 jogadores que mais me deram assistência.</summary>
    public List<SpotlightRelation> MostAssistedBy { get; init; } = new();

    /// <summary>Top 3 jogadores para quem dei mais assistências.</summary>
    public List<SpotlightRelation> MostAssistedTo { get; init; } = new();
}
