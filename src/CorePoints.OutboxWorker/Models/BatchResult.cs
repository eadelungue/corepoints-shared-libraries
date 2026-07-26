namespace CorePoints.OutboxWorker.Models;

public sealed record BatchResult
{
    public int TotalFetched { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public TimeSpan Duration { get; init; }
}
