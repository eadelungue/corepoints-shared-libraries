using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Domain.Entities;
using Dapper;
using Npgsql;

namespace CorePoints.LedgerCore.Infrastructure.Data;

public sealed class AccountRepository(IDbConnectionFactory connectionFactory) : IAccountRepository
{
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id AS Id, holder_name AS HolderName, account_type AS AccountType,
                   balance AS Balance, created_at AS CreatedAt
            FROM accounts
            WHERE id = @Id
            """;

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        return await connection.QueryFirstOrDefaultAsync<Account>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<Account?> GetForUpdateAsync(Guid id, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        const string sql = """
            SELECT id AS Id, holder_name AS HolderName, account_type AS AccountType,
                   balance AS Balance, created_at AS CreatedAt
            FROM accounts
            WHERE id = @Id
            FOR UPDATE
            """;

        return await connection.QueryFirstOrDefaultAsync<Account>(
            new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: ct));
    }

    public async Task InsertAsync(Account account, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO accounts (id, holder_name, account_type, balance, created_at)
            VALUES (@Id, @HolderName, @AccountType, @Balance, @CreatedAt)
            """;

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(
            new CommandDefinition(sql, new
            {
                account.Id,
                account.HolderName,
                AccountType = account.AccountType.ToString(),
                account.Balance,
                account.CreatedAt
            }, cancellationToken: ct));
    }

    public async Task UpdateBalanceAsync(Account account, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        const string sql = "UPDATE accounts SET balance = @Balance WHERE id = @Id";

        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { account.Balance, account.Id }, transaction, cancellationToken: ct));
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM accounts WHERE id = @Id)";

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }
}
