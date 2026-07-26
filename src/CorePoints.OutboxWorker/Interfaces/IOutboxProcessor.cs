using CorePoints.OutboxWorker.Models;

namespace CorePoints.OutboxWorker.Interfaces;

public interface IOutboxProcessor
{
    Task<BatchResult> ProcessBatchAsync(CancellationToken cancellationToken);
}
