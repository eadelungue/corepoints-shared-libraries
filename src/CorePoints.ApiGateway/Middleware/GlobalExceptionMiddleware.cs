using CorePoints.ApiGateway.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace CorePoints.ApiGateway.Middleware;

/// <summary>
/// Middleware that catches all unhandled exceptions and returns RFC 7807 ProblemDetails responses.
/// Maps known exception types to appropriate HTTP status codes.
/// Never exposes stack traces in responses; logs full exception details internally.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);

            var statusCode = ex switch
            {
                Exceptions.ValidationException => StatusCodes.Status400BadRequest,
                NotFoundException => StatusCodes.Status404NotFound,
                ConflictException => StatusCodes.Status409Conflict,
                UnprocessableEntityException => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status500InternalServerError
            };

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = ReasonPhrases.GetReasonPhrase(statusCode),
                Detail = statusCode == StatusCodes.Status500InternalServerError
                    ? "An internal error occurred."
                    : ex.Message,
                Instance = context.Request.Path
            };
            problem.Extensions["correlationId"] = correlationId;

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
