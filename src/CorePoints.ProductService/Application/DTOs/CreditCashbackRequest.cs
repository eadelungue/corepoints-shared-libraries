namespace CorePoints.ProductService.Application.DTOs;

public sealed record CreditCashbackRequest(
    Guid UserAccountId,
    Guid SystemSourceAccountId,
    decimal TransactionAmount,
    string AccountGroup,
    string OriginalTransactionRef);
