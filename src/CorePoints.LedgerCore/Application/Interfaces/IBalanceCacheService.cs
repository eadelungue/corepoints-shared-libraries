namespace CorePoints.LedgerCore.Application.Interfaces;

public interface IBalanceCacheService
{
    Task<decimal?> GetAsync(Guid accountId, CancellationToken ct);
    Task SetAsync(Guid accountId, decimal balance, CancellationToken ct);
    Task InvalidateAsync(Guid accountId, CancellationToken ct);
}
