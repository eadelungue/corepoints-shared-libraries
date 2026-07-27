namespace CorePoints.LedgerCore.Domain.Exceptions;

public sealed class InsufficientBalanceException : Exception
{
    public Guid AccountId { get; }
    public decimal CurrentBalance { get; }
    public decimal RequestedAmount { get; }

    public InsufficientBalanceException(Guid accountId, decimal currentBalance, decimal requestedAmount)
        : base($"Account {accountId} has insufficient balance. Current: {currentBalance}, Requested: {requestedAmount}")
    {
        AccountId = accountId;
        CurrentBalance = currentBalance;
        RequestedAmount = requestedAmount;
    }
}
