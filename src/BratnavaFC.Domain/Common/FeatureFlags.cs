namespace BratnavaFC.Domain.Common;

/// <summary>
/// Chaves temporárias para funcionalidades que permanecem implementadas, mas
/// ainda não devem ser disponibilizadas em produção.
/// </summary>
public static class FeatureFlags
{
    // Propriedade (em vez de const) evita que o compilador descarte o código
    // preservado para a futura reativação da funcionalidade.
    public static bool ExternalPlayerInteractionsEnabled => false;
}
