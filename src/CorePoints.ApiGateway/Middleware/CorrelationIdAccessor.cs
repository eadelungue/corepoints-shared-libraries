using Microsoft.AspNetCore.Http;

namespace CorePoints.ApiGateway.Middleware;

/// <summary>
/// Implementation of ICorrelationIdAccessor using IHttpContextAccessor
/// to retrieve the correlation ID stored in HttpContext.Items.
/// </summary>
public class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CorrelationId =>
        _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString() ?? string.Empty;
}
