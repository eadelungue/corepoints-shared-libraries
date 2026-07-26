namespace CorePoints.LedgerCore.Domain.Entities;

public sealed class OutboxEvent
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
}
