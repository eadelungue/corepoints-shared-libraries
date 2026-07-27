namespace CorePoints.ProductService.Application.DTOs;

public sealed record TransferResponse(Guid TransactionId, decimal Amount);
