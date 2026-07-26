using CorePoints.LedgerCore.Domain.Entities;
using Npgsql;

namespace CorePoints.LedgerCore.Application.Interfaces;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Account?> GetForUpdateAsync(Guid id, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct);
    Task InsertAsync(Account account, CancellationToken ct);
    Task UpdateBalanceAsync(Account account, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
}
