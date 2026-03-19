namespace BratnavaFC.Domain.Enums;

/// <summary>Define como a patota cobra os jogadores.</summary>
public enum PaymentMode
{
    /// <summary>Mensalidade fixa mensal.</summary>
    Monthly = 0,

    /// <summary>Cobrança por jogo — valor definido ao encerrar cada partida.</summary>
    PerGame = 1,
}
