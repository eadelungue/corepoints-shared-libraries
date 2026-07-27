using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Domain.Exceptions;

namespace CorePoints.LedgerCore.Application.UseCases.GetBalance;

public sealed class GetBalanceUseCase(
    IBalanceCacheService balanceCache,
    IAccountRepository accountRepository)
{
    public async Task<BalanceResponse> ExecuteAsync(Guid accountId, CancellationToken ct)
    {
        // 1. Try cache first
        var cached = await balanceCache.GetAsync(accountId, ct);
        if (cached.HasValue)
            return new BalanceResponse(accountId, cached.Value);

        // 2. Cache miss — query DB
        var account = await accountRepository.GetByIdAsync(accountId, ct)
            ?? throw new AccountNotFoundException(accountId);

        // 3. Populate cache (TTL 7s)
        await balanceCache.SetAsync(accountId, account.Balance, ct);

        return new BalanceResponse(accountId, account.Balance);
    }
}
