namespace CorePoints.ProductService.Domain.Entities;

public sealed record CashbackRule(
    Guid Id,
    string Name,
    decimal Percentage,
    decimal MinAmount,
    decimal MaxAmount,
    bool IsActive,
    string[] TargetGroups);
