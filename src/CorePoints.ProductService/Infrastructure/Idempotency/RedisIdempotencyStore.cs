using CorePoints.ProductService.Application.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CorePoints.ProductService.Infrastructure.Idempotency;

public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDbConnectionFactory _connFactory;
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public RedisIdempotencyStore(
        IConnectionMultiplexer redis,
        IDbConnectionFactory connFactory,
        ILogger<RedisIdempotencyStore> logger)
    {
        _redis = redis;
        _connFactory = connFactory;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        // Try Redis first
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync($"product:idempotency:{key}");
            if (value.HasValue) return value.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis idempotency read failed for key {Key}, falling back to DB", key);
        }

        // DB fallback
        try
        {
            using var conn = _connFactory.CreateConnection();
            await conn.OpenAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(
                    "SELECT response_payload FROM idempotency_keys WHERE key = @Key AND expires_at > NOW()",
                    new { Key = key }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DB idempotency read also failed for key {Key}", key);
            return null;
        }
    }

    public async Task SetAsync(string key, string responsePayload, CancellationToken ct = default)
    {
        // Write to Redis
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync($"product:idempotency:{key}", responsePayload, Ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis idempotency write failed for key {Key}", key);
        }

        // Write to DB
        try
        {
            using var conn = _connFactory.CreateConnection();
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO idempotency_keys (key, response_payload, created_at, expires_at)
                  VALUES (@Key, @Payload::jsonb, NOW(), @ExpiresAt)
                  ON CONFLICT (key) DO NOTHING",
                new { Key = key, Payload = responsePayload, ExpiresAt = DateTime.UtcNow.Add(Ttl) },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DB idempotency write failed for key {Key}", key);
        }
    }
}
