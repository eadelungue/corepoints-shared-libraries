using System.Text.Json;
using CorePoints.ProductService.Application.Interfaces;

namespace CorePoints.ProductService.Api.Filters;

public sealed class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var key)
            || string.IsNullOrWhiteSpace(key))
        {
            return Results.Problem(
                title: "Missing Idempotency-Key",
                detail: "The Idempotency-Key header is required for this operation.",
                statusCode: 400);
        }

        var store = httpContext.RequestServices.GetRequiredService<IIdempotencyStore>();
        var existing = await store.GetAsync(key!, httpContext.RequestAborted);
        if (existing is not null)
        {
            httpContext.Response.Headers["X-Idempotent-Replayed"] = "true";
            return Results.Text(existing, "application/json", statusCode: 200);
        }

        // Store the key in Items so endpoints can access it
        httpContext.Items["IdempotencyKey"] = key.ToString();

        return await next(context);
    }
}
