using StackExchange.Redis;

namespace BratnavaFC.Application.Abstractions;

public interface IRedisConnectionProvider
{
    Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken ct = default);
}
