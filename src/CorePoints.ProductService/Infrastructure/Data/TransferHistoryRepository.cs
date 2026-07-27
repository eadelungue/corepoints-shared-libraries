using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Domain.Entities;
using Dapper;
using Npgsql;

namespace CorePoints.ProductService.Infrastructure.Data;

public sealed class TransferHistoryRepository : ITransferHistoryRepository
{
    private readonly IDbConnectionFactory _connFactory;

    public TransferHistoryRepository(IDbConnectionFactory connFactory)
    {
        _connFactory = connFactory;
    }

    public async Task<decimal> GetDailyTotalAsync(Guid sourceAccountId, DateOnly date, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COALESCE(SUM(amount), 0)
            FROM transfer_history
            WHERE source_account_id = @SourceAccountId
              AND created_at::date = @Date";

        using var conn = _connFactory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { SourceAccountId = sourceAccountId, Date = date }, cancellationToken: ct));
    }

    public async Task InsertAsync(TransferHistoryEntry transfer, NpgsqlConnection conn, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO transfer_history (id, source_account_id, destination_account_id, amount, ledger_transaction_id, created_at)
            VALUES (@Id, @SourceAccountId, @DestinationAccountId, @Amount, @LedgerTransactionId, @CreatedAt)";

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            transfer.Id,
            transfer.SourceAccountId,
            transfer.DestinationAccountId,
            transfer.Amount,
            transfer.LedgerTransactionId,
            transfer.CreatedAt
        }, cancellationToken: ct));
    }
}
