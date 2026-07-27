using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Domain.Entities;
using Dapper;
using Npgsql;

namespace CorePoints.LedgerCore.Infrastructure.Data;

public sealed class OutboxRepository : IOutboxRepository
{
    public async Task InsertAsync(OutboxEvent outboxEvent, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO outbox_events (id, event_type, payload, correlation_id, created_at)
            VALUES (@Id, @EventType, @Payload::jsonb, @CorrelationId, @CreatedAt)
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(sql, new
            {
                outboxEvent.Id,
                outboxEvent.EventType,
                outboxEvent.Payload,
                outboxEvent.CorrelationId,
                outboxEvent.CreatedAt
            }, transaction, cancellationToken: ct));
    }
}
