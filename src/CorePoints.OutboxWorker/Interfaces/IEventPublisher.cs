using CorePoints.OutboxWorker.Models;

namespace CorePoints.OutboxWorker.Interfaces;

public interface IEventPublisher
{
    Task<PublishResult> PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}
