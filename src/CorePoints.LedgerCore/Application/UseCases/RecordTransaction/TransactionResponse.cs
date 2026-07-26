namespace CorePoints.LedgerCore.Application.UseCases.RecordTransaction;

public sealed record TransactionResponse(
    Guid Id,
    string IdempotencyKey,
    Guid DebitAccountId,
    Guid CreditAccountId,
    decimal Amount,
    string? Description,
    DateTime CreatedAt);
