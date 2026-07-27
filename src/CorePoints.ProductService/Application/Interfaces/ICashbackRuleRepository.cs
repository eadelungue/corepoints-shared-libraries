using CorePoints.ProductService.Domain.Entities;

namespace CorePoints.ProductService.Application.Interfaces;

public interface ICashbackRuleRepository
{
    Task<CashbackRule?> GetActiveRuleAsync(string accountGroup, CancellationToken ct = default);
}
