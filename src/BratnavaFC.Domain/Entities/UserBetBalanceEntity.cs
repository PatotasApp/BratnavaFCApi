namespace BratnavaFC.Domain.Entities;

public sealed class UserBetBalanceEntity : BaseEntity
{
    /// <summary>
    /// Cada partida credita 200 fichas base. Saldo começa em 0.
    /// Pode ser negativo se o jogador perder mais do que ganhou.
    /// </summary>
    public const int MatchBaseReward = 200;

    public Guid GroupId      { get; private set; }
    public Guid UserId       { get; private set; }
    public int  Balance      { get; private set; } = 0;
    public int  TotalBets    { get; private set; }
    public int  TotalCorrect { get; private set; }

    public UserBetBalanceEntity(Guid groupId, Guid userId)
    {
        GroupId = groupId;
        UserId  = userId;
        Balance = 0;
    }

    /// <summary>Aplica variação (positiva ou negativa). Saldo pode ser negativo.</summary>
    public void ApplyDelta(int delta)
    {
        Balance   += delta;
        UpdateDate = DateTime.UtcNow;
    }

    public void RecordBetResult(int correctSelections)
    {
        TotalBets    += 1;
        TotalCorrect += correctSelections;
        UpdateDate    = DateTime.UtcNow;
    }

    // EF Core
    private UserBetBalanceEntity() { }
}
