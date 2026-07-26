namespace CorePoints.OutboxWorker.Models;

public sealed class OutboxWorkerOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 50;
    public string SnsTopicArn { get; set; } = string.Empty;
    public int MaxRetryAttempts { get; set; } = 3;
    public int HealthCheckPort { get; set; } = 8080;
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public string DatabaseConnectionString { get; set; } = string.Empty;
}
