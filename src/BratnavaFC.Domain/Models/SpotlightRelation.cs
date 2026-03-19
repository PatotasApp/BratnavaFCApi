namespace BratnavaFC.Domain.Models;

public sealed class SpotlightRelation
{
    public Guid   PlayerId { get; init; }
    public string Name     { get; init; } = string.Empty;
    /// <summary>Quantidade de partidas/gols que formam a relação.</summary>
    public int    Count    { get; init; }
    /// <summary>Taxa relevante (win rate together, win rate against, etc.).</summary>
    public double Rate     { get; init; }
}
