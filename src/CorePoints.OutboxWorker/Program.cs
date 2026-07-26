using System.Net;
using Amazon.SimpleNotificationService;
using CorePoints.OutboxWorker.HealthChecks;
using CorePoints.OutboxWorker.Interfaces;
using CorePoints.OutboxWorker.Models;
using CorePoints.OutboxWorker.Publishers;
using CorePoints.OutboxWorker.Repositories;
using CorePoints.OutboxWorker.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Polly;
using Polly.Retry;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog
    builder.Host.UseSerilog((context, serviceProvider, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(serviceProvider)
            .Enrich.FromLogContext()
            .WriteTo.Console(new CompactJsonFormatter());
    });

    // Bind OutboxWorkerOptions from environment variables
    var options = new OutboxWorkerOptions();

    if (int.TryParse(Environment.GetEnvironmentVariable("OUTBOX_POLLING_INTERVAL_SECONDS"), out var pollingInterval))
        options.PollingInterval = TimeSpan.FromSeconds(pollingInterval);

    if (int.TryParse(Environment.GetEnvironmentVariable("OUTBOX_BATCH_SIZE"), out var batchSize))
        options.BatchSize = batchSize;

    var snsTopicArn = Environment.GetEnvironmentVariable("OUTBOX_SNS_TOPIC_ARN");
    if (!string.IsNullOrEmpty(snsTopicArn))
        options.SnsTopicArn = snsTopicArn;

    if (int.TryParse(Environment.GetEnvironmentVariable("OUTBOX_MAX_RETRY_ATTEMPTS"), out var maxRetry))
        options.MaxRetryAttempts = maxRetry;

    if (int.TryParse(Environment.GetEnvironmentVariable("OUTBOX_HEALTH_PORT"), out var healthPort))
        options.HealthCheckPort = healthPort;

    if (int.TryParse(Environment.GetEnvironmentVariable("OUTBOX_SHUTDOWN_TIMEOUT_SECONDS"), out var shutdownTimeout))
        options.ShutdownTimeout = TimeSpan.FromSeconds(shutdownTimeout);

    var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING");
    if (!string.IsNullOrEmpty(connectionString))
        options.DatabaseConnectionString = connectionString;

    builder.Services.Configure<OutboxWorkerOptions>(o =>
    {
        o.PollingInterval = options.PollingInterval;
        o.BatchSize = options.BatchSize;
        o.SnsTopicArn = options.SnsTopicArn;
        o.MaxRetryAttempts = options.MaxRetryAttempts;
        o.HealthCheckPort = options.HealthCheckPort;
        o.ShutdownTimeout = options.ShutdownTimeout;
        o.DatabaseConnectionString = options.DatabaseConnectionString;
    });

    // Register Polly resilience pipelines
    builder.Services.AddResiliencePipeline("sns-publish", pipelineBuilder =>
    {
        pipelineBuilder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder()
                    .Handle<AmazonSimpleNotificationServiceException>(
                        ex => (int)ex.StatusCode >= 500)
                    .Handle<TaskCanceledException>()
                    .Handle<HttpRequestException>()
            })
            .AddTimeout(TimeSpan.FromSeconds(10));
    });

    builder.Services.AddResiliencePipeline("database", pipelineBuilder =>
    {
        pipelineBuilder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(100),
                ShouldHandle = new PredicateBuilder()
                    .Handle<NpgsqlException>(ex => ex.IsTransient)
                    .Handle<TimeoutException>()
            })
            .AddTimeout(TimeSpan.FromSeconds(5));
    });

    // Register AWS SNS client
    builder.Services.AddSingleton<IAmazonSimpleNotificationService, AmazonSimpleNotificationServiceClient>();

    // Register application services
    builder.Services.AddSingleton<IOutboxRepository, OutboxRepository>();
    builder.Services.AddSingleton<IEventPublisher, SnsEventPublisher>();
    builder.Services.AddSingleton<IOutboxProcessor, OutboxProcessor>();

    // Register BackgroundService
    builder.Services.AddHostedService<OutboxWorkerService>();

    // Register health checks
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy,
            tags: new[] { "ready" });

    // Configure shutdown timeout
    builder.Host.ConfigureHostOptions(hostOptions =>
    {
        hostOptions.ShutdownTimeout = options.ShutdownTimeout;
    });

    // Configure Kestrel to listen on health check port
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        kestrel.ListenAnyIP(options.HealthCheckPort);
    });

    var app = builder.Build();

    // Map health endpoint
    app.MapHealthChecks("/health");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Outbox Worker terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
