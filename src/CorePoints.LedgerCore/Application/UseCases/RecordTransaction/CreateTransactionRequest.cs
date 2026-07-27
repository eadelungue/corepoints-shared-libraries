namespace CorePoints.LedgerCore.Application.UseCases.RecordTransaction;

public sealed record CreateTransactionRequest(
    Guid DebitAccountId,
    Guid CreditAccountId,
    decimal Amount,
    string? Description);
