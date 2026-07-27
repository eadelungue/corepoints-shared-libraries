namespace CorePoints.ProductService.Application.DTOs;

public sealed record TransactionDetailResponse(
    Guid Id,
    Guid DebitAccountId,
    Guid CreditAccountId,
    decimal Amount,
    string Description,
    DateTime CreatedAt);
