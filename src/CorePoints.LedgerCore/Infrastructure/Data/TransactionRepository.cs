using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Domain.Entities;
using Dapper;
using Npgsql;

namespace CorePoints.LedgerCore.Infrastructure.Data;

public sealed class TransactionRepository(IDbConnectionFactory connectionFactory) : ITransactionRepository
{
    public async Task InsertAsync(Transaction transaction, NpgsqlConnection connection, NpgsqlTransaction dbTransaction, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO transactions (id, idempotency_key, debit_account_id, credit_account_id, amount, description, created_at)
            VALUES (@Id, @IdempotencyKey, @DebitAccountId, @CreditAccountId, @Amount, @Description, @CreatedAt)
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(sql, new
            {
                transaction.Id,
                transaction.IdempotencyKey,
                transaction.DebitAccountId,
                transaction.CreditAccountId,
                transaction.Amount,
                transaction.Description,
                transaction.CreatedAt
            }, dbTransaction, cancellationToken: ct));
    }

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id AS Id, idempotency_key AS IdempotencyKey, debit_account_id AS DebitAccountId,
                   credit_account_id AS CreditAccountId, amount AS Amount, description AS Description,
                   created_at AS CreatedAt
            FROM transactions
            WHERE id = @Id
            """;

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        return await connection.QueryFirstOrDefaultAsync<Transaction>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<IEnumerable<Transaction>> GetByAccountIdPaginatedAsync(Guid accountId, int page, int pageSize, CancellationToken ct)
    {
        const string sql = """
            SELECT id AS Id, idempotency_key AS IdempotencyKey, debit_account_id AS DebitAccountId,
                   credit_account_id AS CreditAccountId, amount AS Amount, description AS Description,
                   created_at AS CreatedAt
            FROM transactions
            WHERE debit_account_id = @AccountId OR credit_account_id = @AccountId
            ORDER BY created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var offset = (page - 1) * pageSize;

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        return await connection.QueryAsync<Transaction>(
            new CommandDefinition(sql, new { AccountId = accountId, PageSize = pageSize, Offset = offset }, cancellationToken: ct));
    }

    public async Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM transactions
            WHERE debit_account_id = @AccountId OR credit_account_id = @AccountId
            """;

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { AccountId = accountId }, cancellationToken: ct));
    }
}
