namespace BratnavaFC.Domain.Enums;

/// <summary>O que a planilha enviada pela patota contém.</summary>
public enum GroupSpreadsheetKind
{
    /// <summary>Histórico de jogos (partidas, placares, gols).</summary>
    Matches = 0,

    /// <summary>Pagamentos (mensalidades, avulsos, caixa).</summary>
    Payments = 1,
}
