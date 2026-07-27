using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Domain.Entities;
using Dapper;

namespace CorePoints.ProductService.Infrastructure.Data;

public sealed class CashbackRuleRepository : ICashbackRuleRepository
{
    private readonly IDbConnectionFactory _connFactory;

    public CashbackRuleRepository(IDbConnectionFactory connFactory)
    {
        _connFactory = connFactory;
    }

    public async Task<CashbackRule?> GetActiveRuleAsync(string accountGroup, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id AS Id, name AS Name, percentage AS Percentage,
                   min_amount AS MinAmount, max_amount AS MaxAmount,
                   is_active AS IsActive, target_groups AS TargetGroups
            FROM cashback_rules
            WHERE is_active = true AND @AccountGroup = ANY(target_groups)
            ORDER BY percentage DESC
            LIMIT 1";

        using var conn = _connFactory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<CashbackRule>(
            new CommandDefinition(sql, new { AccountGroup = accountGroup }, cancellationToken: ct));
    }
}
