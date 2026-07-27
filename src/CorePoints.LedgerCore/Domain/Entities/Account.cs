using CorePoints.LedgerCore.Domain.Exceptions;
using CorePoints.LedgerCore.Domain.ValueObjects;

namespace CorePoints.LedgerCore.Domain.Entities;

public sealed class Account
{
    public Guid Id { get; init; }
    public string HolderName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; init; }

    public void Debit(decimal amount)
    {
        if (Balance < amount)
            throw new InsufficientBalanceException(Id, Balance, amount);

        Balance -= amount;
    }

    public void Credit(decimal amount)
    {
        Balance += amount;
    }
}
