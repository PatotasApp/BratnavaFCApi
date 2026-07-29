namespace BratnavaFC.Infrastructure;

/// <summary>
/// Lançada quando uma dependência de infraestrutura foi intencionalmente desabilitada no
/// ambiente e a operação pedida não tem substituto plausível — leitura de um objeto que
/// nunca foi gravado, por exemplo. Mapeada para HTTP 503 no exception handler global.
/// </summary>
public sealed class DependencyDisabledException : Exception
{
    public DependencyDisabledException(string dependency, string environmentName)
        : base($"{dependency} está desabilitado no ambiente {environmentName}.")
    {
        Dependency = dependency;
        EnvironmentName = environmentName;
    }

    public string Dependency { get; }
    public string EnvironmentName { get; }
}
