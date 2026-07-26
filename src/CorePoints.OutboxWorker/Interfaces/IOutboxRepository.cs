using CorePoints.OutboxWorker.Models;

namespace CorePoints.OutboxWorker.Interfaces;

public interface IOutboxRepository
{
    Task<IReadOnlyList<OutboxEvent>> GetUnpublishedEventsAsync(
        int batchSize, CancellationToken cancellationToken);

    Task MarkAsPublishedAsync(
        Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken);
}
