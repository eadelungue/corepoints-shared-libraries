namespace CorePoints.ProductService.Application.DTOs;

public sealed record TransferRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string SourceAccountType);
