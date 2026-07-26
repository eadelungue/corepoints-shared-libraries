using System.Data;
using CorePoints.OutboxWorker.Interfaces;
using CorePoints.OutboxWorker.Models;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CorePoints.OutboxWorker.Repositories;

public sealed class OutboxRepository : IOutboxRepository
{
    private readonly OutboxWorkerOptions _options;
    private readonly ILogger<OutboxRepository> _logger;

    public OutboxRepository(
        IOptions<OutboxWorkerOptions> options,
        ILogger<OutboxRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OutboxEvent>> GetUnpublishedEventsAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id AS Id, 
                   event_type AS EventType, 
                   payload AS Payload, 
                   correlation_id AS CorrelationId, 
                   created_at AS CreatedAt, 
                   published_at AS PublishedAt
            FROM outbox_events 
            WHERE published_at IS NULL 
            ORDER BY created_at ASC 
            LIMIT @BatchSize
            """;

        await using var connection = new NpgsqlConnection(_options.DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            sql,
            new { BatchSize = batchSize },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<OutboxEvent>(command);
        return results.ToList().AsReadOnly();
    }

    public async Task MarkAsPublishedAsync(
        Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE outbox_events 
            SET published_at = @PublishedAt 
            WHERE id = @EventId
            """;

        await using var connection = new NpgsqlConnection(_options.DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            sql,
            new { EventId = eventId, PublishedAt = publishedAt },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
