namespace CorePoints.ApiGateway.Middleware;

/// <summary>
/// Provides access to the current request's correlation ID via dependency injection.
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// Gets the correlation ID for the current request.
    /// </summary>
    string CorrelationId { get; }
}
