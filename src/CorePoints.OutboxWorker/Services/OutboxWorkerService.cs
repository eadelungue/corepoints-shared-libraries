using CorePoints.OutboxWorker.Interfaces;
using CorePoints.OutboxWorker.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CorePoints.OutboxWorker.Services;

public sealed class OutboxWorkerService : BackgroundService
{
    private readonly IOutboxProcessor _processor;
    private readonly OutboxWorkerOptions _options;
    private readonly ILogger<OutboxWorkerService> _logger;

    public OutboxWorkerService(
        IOutboxProcessor processor,
        IOptions<OutboxWorkerOptions> options,
        ILogger<OutboxWorkerService> logger)
    {
        _processor = processor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox Worker started. PollingInterval={PollingInterval}s, BatchSize={BatchSize}, " +
            "SnsTopicArn={SnsTopicArn}, MaxRetryAttempts={MaxRetryAttempts}, " +
            "HealthCheckPort={HealthCheckPort}, ShutdownTimeout={ShutdownTimeout}s",
            _options.PollingInterval.TotalSeconds,
            _options.BatchSize,
            _options.SnsTopicArn,
            _options.MaxRetryAttempts,
            _options.HealthCheckPort,
            _options.ShutdownTimeout.TotalSeconds);

        using var timer = new PeriodicTimer(_options.PollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Outbox Worker shutting down gracefully");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error during outbox processing cycle. Will retry at next interval");
            }
        }

        _logger.LogInformation("Outbox Worker stopped");
    }
}
