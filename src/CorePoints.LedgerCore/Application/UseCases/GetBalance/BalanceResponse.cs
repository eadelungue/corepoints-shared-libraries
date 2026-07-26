namespace CorePoints.LedgerCore.Application.UseCases.GetBalance;

public sealed record BalanceResponse(Guid AccountId, decimal Balance);
