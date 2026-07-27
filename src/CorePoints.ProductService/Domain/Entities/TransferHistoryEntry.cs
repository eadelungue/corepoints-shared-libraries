namespace CorePoints.ProductService.Domain.Entities;

public sealed record TransferHistoryEntry(
    Guid Id,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    Guid LedgerTransactionId,
    DateTime CreatedAt);
