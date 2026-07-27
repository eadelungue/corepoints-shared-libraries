namespace CorePoints.ProductService.Application.DTOs;

public sealed record StatementResponse(
    List<StatementItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record StatementItem(
    Guid Id,
    decimal Amount,
    string Description,
    DateTime CreatedAt);
