namespace CorePoints.ProductService.Application.DTOs;

public sealed record LedgerTransactionResult(
    Guid TransactionId,
    decimal Amount,
    string Description,
    DateTime CreatedAt);

public sealed record LedgerBalanceResult(
    Guid AccountId,
    decimal Balance);

public sealed record LedgerStatementResult(
    List<LedgerStatementItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record LedgerStatementItem(
    Guid Id,
    decimal Amount,
    string Description,
    DateTime CreatedAt);

public sealed record LedgerTransactionDetailResult(
    Guid Id,
    Guid DebitAccountId,
    Guid CreditAccountId,
    decimal Amount,
    string Description,
    DateTime CreatedAt);
