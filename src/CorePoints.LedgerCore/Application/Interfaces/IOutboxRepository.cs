using CorePoints.LedgerCore.Domain.Entities;
using Npgsql;

namespace CorePoints.LedgerCore.Application.Interfaces;

public interface IOutboxRepository
{
    Task InsertAsync(OutboxEvent outboxEvent, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct);
}
