using CorePoints.ProductService.Domain.Entities;
using Npgsql;

namespace CorePoints.ProductService.Application.Interfaces;

public interface ITransferHistoryRepository
{
    Task<decimal> GetDailyTotalAsync(Guid sourceAccountId, DateOnly date, CancellationToken ct = default);
    Task InsertAsync(TransferHistoryEntry transfer, NpgsqlConnection conn, CancellationToken ct = default);
}
