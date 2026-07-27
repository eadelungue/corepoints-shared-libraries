using CorePoints.ProductService.Domain.Entities;
using CorePoints.ProductService.Domain.Exceptions;

namespace CorePoints.ProductService.Domain.Services;

public static class CashbackCalculator
{
    public static decimal Calculate(decimal transactionAmount, CashbackRule rule)
    {
        if (transactionAmount < rule.MinAmount || transactionAmount > rule.MaxAmount)
            throw new IneligibleCashbackException(
                $"Transaction amount {transactionAmount} is outside the eligible range [{rule.MinAmount}, {rule.MaxAmount}].");

        return transactionAmount * (rule.Percentage / 100m);
    }

    public static bool IsEligible(string accountGroup, CashbackRule rule)
        => rule.IsActive && rule.TargetGroups.Contains(accountGroup);
}
