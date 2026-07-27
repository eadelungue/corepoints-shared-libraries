using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Domain.Entities;
using Dapper;
using Npgsql;

namespace CorePoints.ProductService.Infrastructure.Data;

public sealed class OutboxRepository : IOutboxRepository
{
    public async Task InsertAsync(OutboxEvent outboxEvent, NpgsqlConnection connection, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO outbox_events (id, event_type, payload, correlation_id, created_at, retry_count)
            VALUES (@Id, @EventType, @Payload::jsonb, @CorrelationId, NOW(), 0)";

        var id = outboxEvent.Id == Guid.Empty ? Guid.NewGuid() : outboxEvent.Id;

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            outboxEvent.EventType,
            outboxEvent.Payload,
            outboxEvent.CorrelationId
        }, cancellationToken: ct));
    }
}
