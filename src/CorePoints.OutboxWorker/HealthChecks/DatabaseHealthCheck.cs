using CorePoints.OutboxWorker.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CorePoints.OutboxWorker.HealthChecks;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly OutboxWorkerOptions _options;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(
        IOptions<OutboxWorkerOptions> options,
        ILogger<DatabaseHealthCheck> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_options.DatabaseConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("Database connection is healthy");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: unable to connect to database");
            return HealthCheckResult.Unhealthy("Database connection failed", ex);
        }
    }
}
