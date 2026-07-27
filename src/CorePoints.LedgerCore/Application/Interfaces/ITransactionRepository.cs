using CorePoints.LedgerCore.Domain.Entities;
using Npgsql;

namespace CorePoints.LedgerCore.Application.Interfaces;

public interface ITransactionRepository
{
    Task InsertAsync(Transaction transaction, NpgsqlConnection connection, NpgsqlTransaction dbTransaction, CancellationToken ct);
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IEnumerable<Transaction>> GetByAccountIdPaginatedAsync(Guid accountId, int page, int pageSize, CancellationToken ct);
    Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken ct);
}
