using CorePoints.ProductService.Application.Interfaces;
using CorePoints.Resilience.Clients;
using Dapper;
using StackExchange.Redis;

namespace CorePoints.ProductService.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous()
            .WithName("LivenessCheck")
            .WithTags("Health");

        app.MapGet("/health/ready", async (
            IDbConnectionFactory connFactory,
            IConnectionMultiplexer redis,
            ILedgerClient ledgerClient,
            CancellationToken ct) =>
        {
            var checks = new Dictionary<string, string>();

            // PostgreSQL check
            try
            {
                using var conn = connFactory.CreateConnection();
                await conn.OpenAsync(ct);
                await conn.ExecuteScalarAsync(new CommandDefinition("SELECT 1", cancellationToken: ct));
                checks["postgresql"] = "Healthy";
            }
            catch
            {
                checks["postgresql"] = "Unhealthy";
            }

            // Redis check
            try
            {
                await redis.GetDatabase().PingAsync();
                checks["redis"] = "Healthy";
            }
            catch
            {
                checks["redis"] = "Unhealthy";
            }

            // Ledger check
            try
            {
                var resp = await ledgerClient.GetBalanceAsync("health-probe", "", ct);
                checks["ledger"] = resp.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable
                    ? "Healthy" : "Unhealthy";
            }
            catch
            {
                checks["ledger"] = "Unhealthy";
            }

            var allHealthy = checks.Values.All(v => v == "Healthy");
            return allHealthy
                ? Results.Ok(new { status = "Healthy", checks })
                : Results.Json(new { status = "Unhealthy", checks }, statusCode: 503);
        })
        .AllowAnonymous()
        .WithName("ReadinessCheck")
        .WithTags("Health");
    }
}
