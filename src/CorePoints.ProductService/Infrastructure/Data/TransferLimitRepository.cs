using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Domain.Entities;
using Dapper;

namespace CorePoints.ProductService.Infrastructure.Data;

public sealed class TransferLimitRepository : ITransferLimitRepository
{
    private readonly IDbConnectionFactory _connFactory;

    public TransferLimitRepository(IDbConnectionFactory connFactory)
    {
        _connFactory = connFactory;
    }

    public async Task<TransferLimit?> GetByAccountTypeAsync(string accountType, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id AS Id, account_type AS AccountType,
                   daily_limit AS DailyLimit,
                   per_transaction_limit AS PerTransactionLimit
            FROM transfer_limits
            WHERE account_type = @AccountType";

        using var conn = _connFactory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<TransferLimit>(
            new CommandDefinition(sql, new { AccountType = accountType }, cancellationToken: ct));
    }
}
