namespace CorePoints.ProductService.Domain.Entities;

public sealed record TransferLimit(
    Guid Id,
    string AccountType,
    decimal DailyLimit,
    decimal PerTransactionLimit);
