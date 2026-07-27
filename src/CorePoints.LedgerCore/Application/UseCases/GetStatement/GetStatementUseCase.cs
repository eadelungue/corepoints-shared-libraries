using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Application.UseCases.RecordTransaction;
using CorePoints.LedgerCore.Domain.Exceptions;

namespace CorePoints.LedgerCore.Application.UseCases.GetStatement;

public sealed class GetStatementUseCase(
    IAccountRepository accountRepository,
    ITransactionRepository transactionRepository)
{
    public async Task<PaginatedStatementResponse> ExecuteAsync(StatementRequest request, CancellationToken ct)
    {
        var exists = await accountRepository.ExistsAsync(request.AccountId, ct);
        if (!exists)
            throw new AccountNotFoundException(request.AccountId);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 20 : Math.Min(request.PageSize, 100);

        var transactions = await transactionRepository.GetByAccountIdPaginatedAsync(
            request.AccountId, page, pageSize, ct);

        var totalCount = await transactionRepository.CountByAccountIdAsync(request.AccountId, ct);

        var items = transactions.Select(t => new TransactionResponse(
            t.Id,
            t.IdempotencyKey,
            t.DebitAccountId,
            t.CreditAccountId,
            t.Amount,
            t.Description,
            t.CreatedAt));

        return new PaginatedStatementResponse(items, page, pageSize, totalCount);
    }
}
