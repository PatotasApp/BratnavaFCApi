namespace BratnavaFC.Domain.Enums;

public enum ProfileVisibility : short
{
    AuthenticatedUsers = 1,
    SharedPatotas = 2,
    Private = 3,
    /// <summary>Qualquer pessoa com o link, inclusive sem login (vitrine reduzida). Opt-in.</summary>
    Public = 4,
}
