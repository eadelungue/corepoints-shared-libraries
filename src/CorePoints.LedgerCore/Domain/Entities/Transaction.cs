namespace CorePoints.LedgerCore.Domain.Entities;

public sealed class Transaction
{
    public Guid Id { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public Guid DebitAccountId { get; init; }
    public Guid CreditAccountId { get; init; }
    public decimal Amount { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
}
