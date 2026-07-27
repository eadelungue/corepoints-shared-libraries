namespace CorePoints.LedgerCore.Application.UseCases.GetStatement;

public sealed record StatementRequest(Guid AccountId, int Page = 1, int PageSize = 20);
