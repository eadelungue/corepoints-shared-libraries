using CorePoints.ProductService.Domain.Entities;
using CorePoints.ProductService.Domain.Exceptions;

namespace CorePoints.ProductService.Domain.Services;

public static class TransferValidator
{
    public static void Validate(
        decimal amount,
        decimal dailyTotalSoFar,
        TransferLimit limit)
    {
        if (amount > limit.PerTransactionLimit)
            throw new TransferLimitExceededException(
                $"Amount {amount} exceeds per-transaction limit of {limit.PerTransactionLimit}.");

        if (dailyTotalSoFar + amount > limit.DailyLimit)
            throw new TransferLimitExceededException(
                $"Transfer would exceed daily limit of {limit.DailyLimit}. Current daily total: {dailyTotalSoFar}.");
    }
}
