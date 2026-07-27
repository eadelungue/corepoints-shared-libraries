namespace CorePoints.ProductService.Application.DTOs;

public sealed record BalanceResponse(Guid AccountId, decimal Balance);
