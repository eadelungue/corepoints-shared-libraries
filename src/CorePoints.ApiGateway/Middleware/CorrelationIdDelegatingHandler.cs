namespace CorePoints.ApiGateway.Middleware;

/// <summary>
/// DelegatingHandler that attaches the X-Correlation-ID header to all outgoing HttpClient requests.
/// </summary>
public class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public CorrelationIdDelegatingHandler(ICorrelationIdAccessor correlationIdAccessor)
    {
        _correlationIdAccessor = correlationIdAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = _correlationIdAccessor.CorrelationId;

        if (!string.IsNullOrEmpty(correlationId))
        {
            request.Headers.Remove(HeaderName);
            request.Headers.Add(HeaderName, correlationId);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
