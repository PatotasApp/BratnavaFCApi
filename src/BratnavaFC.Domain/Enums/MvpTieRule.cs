namespace BratnavaFC.Domain.Enums;

/// <summary>Define como a patota lida com empates na votação de MVP.</summary>
public enum MvpTieRule : short
{
    /// <summary>Se houver empate, nenhum jogador recebe MVP.</summary>
    NoMvp = 0,

    /// <summary>Se houver empate, todos os empatados recebem MVP.</summary>
    AllMvp = 1,

    /// <summary>
    /// Se houver empate, todos os empatados recebem MVP — desde que o número
    /// de empatados não ultrapasse <see cref="GroupSettingsEntity.MvpTieMaxPlayers"/>.
    /// Caso ultrapasse, nenhum recebe MVP.
    /// </summary>
    AllMvpUpToMax = 2,
}
