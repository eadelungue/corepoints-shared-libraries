namespace CorePoints.ProductService.Application.DTOs;

public sealed record CashbackResponse(Guid TransactionId, decimal CashbackAmount);
