using CorePoints.ProductService.Domain.Entities;
using Npgsql;

namespace CorePoints.ProductService.Application.Interfaces;

public interface IOutboxRepository
{
    Task InsertAsync(OutboxEvent outboxEvent, NpgsqlConnection connection, CancellationToken ct = default);
}
