using System.Net;
using System.Text.Json;
using CorePoints.ProductService.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;

namespace CorePoints.ProductService.Api.Middleware;

public sealed class GlobalExceptionMiddleware
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
            _logger.LogError(ex, "Unhandled exception processing request {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            IneligibleCashbackException => ((int)HttpStatusCode.UnprocessableEntity, "Ineligible Cashback"),
            TransferLimitExceededException => ((int)HttpStatusCode.UnprocessableEntity, "Transfer Limit Exceeded"),
            InsufficientBalanceException => ((int)HttpStatusCode.UnprocessableEntity, "Insufficient Balance"),
            AccountNotFoundException => ((int)HttpStatusCode.NotFound, "Account Not Found"),
            AccountAccessDeniedException => ((int)HttpStatusCode.Forbidden, "Access Denied"),
            LedgerUnavailableException => ((int)HttpStatusCode.ServiceUnavailable, "Service Unavailable"),
            BrokenCircuitException => ((int)HttpStatusCode.ServiceUnavailable, "Service Unavailable"),
            _ => ((int)HttpStatusCode.InternalServerError, "Internal Server Error")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
