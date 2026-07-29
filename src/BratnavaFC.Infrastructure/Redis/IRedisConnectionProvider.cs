using StackExchange.Redis;

namespace BratnavaFC.Infrastructure.Redis;

public interface IRedisConnectionProvider
{
    Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken ct = default);
}
