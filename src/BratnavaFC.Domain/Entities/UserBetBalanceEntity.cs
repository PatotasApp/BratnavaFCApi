namespace BratnavaFC.Domain.Entities;

public sealed class UserBetBalanceEntity : BaseEntity
{
    /// <summary>Saldo acumula apenas lucros/perdas das apostas. Começa em 0.</summary>
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

    public void ForceSetBalance(int newBalance)
    {
        Balance    = newBalance;
        UpdateDate = DateTime.UtcNow;
    }

    public void ReverseBetResult(int correctSelections, int delta)
    {
        Balance      -= delta;
        TotalBets    -= 1;
        TotalCorrect -= correctSelections;
        UpdateDate    = DateTime.UtcNow;
    }

    // EF Core
    private UserBetBalanceEntity() { }
}
