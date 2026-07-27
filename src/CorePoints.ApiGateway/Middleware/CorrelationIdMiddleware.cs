using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace CorePoints.ApiGateway.Middleware;

/// <summary>
/// Middleware that extracts or generates a correlation ID for request tracing.
/// Reads X-Correlation-ID from request headers, generates UUID v4 if absent,
/// stores in HttpContext.Items, adds to response headers, and pushes to Serilog LogContext.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
