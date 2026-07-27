using CorePoints.LedgerCore.Application.UseCases.RecordTransaction;

namespace CorePoints.LedgerCore.Application.UseCases.GetStatement;

public sealed record PaginatedStatementResponse(
    IEnumerable<TransactionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
