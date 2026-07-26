using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Polly;
using Polly.Retry;

namespace CorePoints.LedgerCore.Infrastructure.Resilience;

public static class PollyPipelineConfiguration
{
    public static IServiceCollection AddResiliencePipelines(this IServiceCollection services)
    {
        services.AddResiliencePipeline("db-retry", pipeline =>
        {
            pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(ex => ex.IsTransient)
            });
        });

        return services;
    }
}
