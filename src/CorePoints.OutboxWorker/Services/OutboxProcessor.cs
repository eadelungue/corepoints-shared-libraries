using System.Diagnostics;
using CorePoints.OutboxWorker.Interfaces;
using CorePoints.OutboxWorker.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CorePoints.OutboxWorker.Services;

public sealed class OutboxProcessor : IOutboxProcessor
{
    private readonly IOutboxRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly OutboxWorkerOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IOutboxRepository repository,
        IEventPublisher publisher,
        IOptions<OutboxWorkerOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BatchResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var events = await _repository.GetUnpublishedEventsAsync(
            _options.BatchSize, cancellationToken);

        if (events.Count == 0)
        {
            stopwatch.Stop();
            return new BatchResult
            {
                TotalFetched = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Duration = stopwatch.Elapsed
            };
        }

        var successCount = 0;
        var failureCount = 0;

        foreach (var outboxEvent in events)
        {
            var result = await _publisher.PublishAsync(outboxEvent, cancellationToken);

            if (result.Success)
            {
                await _repository.MarkAsPublishedAsync(
                    outboxEvent.Id,
                    DateTimeOffset.UtcNow,
                    cancellationToken);

                successCount++;

                _logger.LogInformation(
                    "Published event {EventId} of type {EventType} with MessageId {MessageId}",
                    outboxEvent.Id, outboxEvent.EventType, result.MessageId);
            }
            else
            {
                failureCount++;

                _logger.LogError(
                    "Failed to publish event {EventId} of type {EventType}: {ErrorMessage}",
                    outboxEvent.Id, outboxEvent.EventType, result.ErrorMessage);
            }
        }

        stopwatch.Stop();

        var batchResult = new BatchResult
        {
            TotalFetched = events.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            Duration = stopwatch.Elapsed
        };

        _logger.LogInformation(
            "Batch completed: {TotalFetched} fetched, {SuccessCount} published, {FailureCount} failed, duration {Duration}ms",
            batchResult.TotalFetched, batchResult.SuccessCount,
            batchResult.FailureCount, batchResult.Duration.TotalMilliseconds);

        return batchResult;
    }
}
