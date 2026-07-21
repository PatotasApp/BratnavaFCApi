using BratnavaFC.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BratnavaFC.Application.Services;

public sealed class RedisConnectionProvider : IRedisConnectionProvider, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<RedisConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnectionMultiplexer? _connection;

    public RedisConnectionProvider(string connectionString, ILogger<RedisConnectionProvider> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection is { IsConnected: true })
            return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsConnected: true })
                return _connection;

            _connection?.Dispose();
            _connection = null;

            var options = ConfigurationOptions.Parse(_connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 10_000;
            options.SyncTimeout = 40_000;

            _connection = await ConnectionMultiplexer.ConnectAsync(options);

            if (!_connection.IsConnected)
            {
                _logger.LogWarning("[Redis] Conexão criada, mas ainda sem endpoints conectados.");
                return null;
            }

            _logger.LogInformation("[Redis] Conexão estabelecida.");
            return _connection;
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or RedisException or ArgumentException)
        {
            _logger.LogWarning(ex, "[Redis] Indisponível. Recursos de replay/fila serão retomados quando a conexão voltar.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _gate.Dispose();
    }
}
