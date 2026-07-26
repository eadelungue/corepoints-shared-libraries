using System.Text.Json;
using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Application.UseCases.GetTransaction;
using CorePoints.LedgerCore.Application.UseCases.RecordTransaction;

namespace CorePoints.LedgerCore.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transactions", async (
            HttpContext httpContext,
            CreateTransactionRequest request,
            RecordTransactionUseCase useCase,
            IIdempotencyStore idempotencyStore,
            CancellationToken ct) =>
        {
            // Extract Idempotency-Key header
            if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyHeader)
                || string.IsNullOrWhiteSpace(idempotencyKeyHeader))
            {
                return Results.Problem(
                    title: "Missing Idempotency-Key",
                    detail: "The Idempotency-Key header is required for transaction operations.",
                    statusCode: 400);
            }

            var idempotencyKey = idempotencyKeyHeader.ToString();

            // Check idempotency store
            var existingResponse = await idempotencyStore.GetAsync(idempotencyKey, ct);
            if (existingResponse is not null)
            {
                var cached = JsonSerializer.Deserialize<TransactionResponse>(existingResponse);
                return Results.Ok(cached);
            }

            var correlationId = httpContext.Items["CorrelationId"]?.ToString()
                ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(request, idempotencyKey, correlationId, ct);

            // Store in idempotency store
            await idempotencyStore.SetAsync(idempotencyKey, JsonSerializer.Serialize(response), ct);

            return Results.Created($"/transactions/{response.Id}", response);
        })
        .WithName("RecordTransaction")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .Produces<TransactionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/transactions/{id:guid}", async (
            Guid id,
            GetTransactionUseCase useCase,
            CancellationToken ct) =>
        {
            var response = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(response);
        })
        .WithName("GetTransaction")
        .Produces<TransactionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
