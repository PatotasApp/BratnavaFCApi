using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Uma categoria apostada dentro de uma aposta.
///
/// PredictedValue formats:
///   WinningTeam   → "TeamA" | "TeamB" | "Draw"
///   FinalScore    → "{teamAGoals}:{teamBGoals}"   ex: "2:1"
///   PlayerGoals   → "{matchPlayerId}|{count}"     ex: "3fa8...afa6|2"
///   PlayerAssists → "{matchPlayerId}|{count}"     ex: "3fa8...afa6|1"
/// </summary>
public sealed class MatchBetSelectionEntity : BaseEntity
{
    public Guid            BetId          { get; private set; }
    public MatchBetEntity? Bet            { get; private set; }
    public BetCategory     Category       { get; private set; }
    public string          PredictedValue { get; private set; } = "";
    public int             FichasWagered  { get; private set; }

    // Preenchidos na resolução
    public string? ActualValue    { get; private set; }
    public int?    FichasEarned   { get; private set; }
    public bool?   IsCorrect      { get; private set; }
    public bool?   IsPartialCredit { get; private set; }

    public MatchBetSelectionEntity(
        Guid betId, BetCategory category, string predictedValue, int fichasWagered)
    {
        BetId          = betId;
        Category       = category;
        PredictedValue = predictedValue;
        FichasWagered  = fichasWagered;
    }

    public void Resolve(int fichasEarned, bool isCorrect, bool isPartialCredit, string actualValue)
    {
        FichasEarned    = fichasEarned;
        IsCorrect       = isCorrect;
        IsPartialCredit = isPartialCredit;
        ActualValue     = actualValue;
        UpdateDate      = DateTime.UtcNow;
    }

    // EF Core
    private MatchBetSelectionEntity() { }
}
