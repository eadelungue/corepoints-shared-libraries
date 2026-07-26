using CorePoints.LedgerCore.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CorePoints.LedgerCore.Infrastructure.Cache;

public sealed class RedisBalanceCacheService(
    IConnectionMultiplexer redis,
    ILogger<RedisBalanceCacheService> logger) : IBalanceCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(7);

    public async Task<decimal?> GetAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync($"balance:{accountId}");
            return value.HasValue ? decimal.Parse(value!) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis cache read failed for account {AccountId}, falling back to DB", accountId);
            return null;
        }
    }

    public async Task SetAsync(Guid accountId, decimal balance, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync($"balance:{accountId}", balance.ToString(), Ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis cache write failed for account {AccountId}", accountId);
        }
    }

    public async Task InvalidateAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.KeyDeleteAsync($"balance:{accountId}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis cache invalidation failed for account {AccountId}", accountId);
        }
    }
}
