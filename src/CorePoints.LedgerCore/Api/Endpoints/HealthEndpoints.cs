using CorePoints.LedgerCore.Application.Interfaces;
using Dapper;
using StackExchange.Redis;

namespace CorePoints.LedgerCore.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .WithName("LivenessProbe")
            .ExcludeFromDescription();

        app.MapGet("/health/ready", async (
            IDbConnectionFactory connectionFactory,
            IConnectionMultiplexer redis,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var checks = new Dictionary<string, string>();

            // PostgreSQL check
            try
            {
                using var conn = connectionFactory.CreateConnection();
                await conn.OpenAsync(ct);
                await conn.ExecuteScalarAsync(new CommandDefinition("SELECT 1", cancellationToken: ct));
                checks["postgresql"] = "Healthy";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PostgreSQL readiness check failed");
                checks["postgresql"] = "Unhealthy";
            }

            // Redis check
            try
            {
                var db = redis.GetDatabase();
                await db.PingAsync();
                checks["redis"] = "Healthy";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Redis readiness check failed");
                checks["redis"] = "Unhealthy";
            }

            var allHealthy = checks.Values.All(v => v == "Healthy");
            var result = new { status = allHealthy ? "Healthy" : "Unhealthy", checks };

            return allHealthy ? Results.Ok(result) : Results.Json(result, statusCode: 503);
        })
        .WithName("ReadinessProbe")
        .ExcludeFromDescription();
    }
}
