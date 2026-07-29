using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BratnavaFC.Infrastructure.Redis;

/// <summary>
/// Registrado quando o Redis está desabilitado no ambiente. Devolver null é o contrato
/// que <see cref="IRedisConnectionProvider"/> já usa para "indisponível", então todos os
/// consumidores existentes já tratam esse caso.
/// </summary>
public sealed class NoOpRedisConnectionProvider : IRedisConnectionProvider
{
    private readonly ILogger<NoOpRedisConnectionProvider> _logger;
    private readonly string _environmentName;
    private bool _logged;

    public NoOpRedisConnectionProvider(
        ILogger<NoOpRedisConnectionProvider> logger,
        string environmentName)
    {
        _logger = logger;
        _environmentName = environmentName;
    }

    public Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken ct = default)
    {
        if (!_logged)
        {
            _logged = true;
            _logger.LogInformation(
                "[Redis] Desabilitado no ambiente {Environment}. Nenhuma conexao sera aberta.",
                _environmentName);
        }

        return Task.FromResult<IConnectionMultiplexer?>(null);
    }
}
