using System.Text.Json;
using CorePoints.LedgerCore.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace CorePoints.LedgerCore.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occurred. TraceId: {TraceId}",
                context.TraceIdentifier);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? context.TraceIdentifier;

        var (statusCode, title, detail) = exception switch
        {
            AccountNotFoundException ex => (StatusCodes.Status404NotFound, "Account Not Found", ex.Message),
            TransactionNotFoundException ex => (StatusCodes.Status404NotFound, "Transaction Not Found", ex.Message),
            InsufficientBalanceException ex => (StatusCodes.Status422UnprocessableEntity, "Insufficient Balance", ex.Message),
            ArgumentException ex => (StatusCodes.Status400BadRequest, "Validation Error", ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["correlationId"] = correlationId
            }
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsJsonAsync(problemDetails, options);
    }
}
