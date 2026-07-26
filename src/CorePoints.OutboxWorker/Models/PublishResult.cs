namespace CorePoints.OutboxWorker.Models;

public sealed record PublishResult
{
    public bool Success { get; init; }
    public string? MessageId { get; init; }
    public string? ErrorMessage { get; init; }
}
