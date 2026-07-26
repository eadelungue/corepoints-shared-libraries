using CorePoints.LedgerCore.Application.Interfaces;
using StackExchange.Redis;

namespace CorePoints.LedgerCore.Infrastructure.Idempotency;

public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync($"idempotency:{key}");
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetAsync(string key, string responsePayload, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync($"idempotency:{key}", responsePayload, Ttl);
    }
}
