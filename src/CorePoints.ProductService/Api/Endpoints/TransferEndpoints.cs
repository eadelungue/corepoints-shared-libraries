using System.Text.Json;
using CorePoints.ProductService.Api.Filters;
using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Application.UseCases;

namespace CorePoints.ProductService.Api.Endpoints;

public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/transfers", async (
            HttpContext ctx,
            TransferRequest request,
            ExecuteTransferUseCase useCase,
            IIdempotencyStore idempotencyStore,
            CancellationToken ct) =>
        {
            var idempotencyKey = ctx.Items["IdempotencyKey"]?.ToString()!;
            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(request, correlationId, ct);

            await idempotencyStore.SetAsync(idempotencyKey,
                JsonSerializer.Serialize(response), ct);

            return Results.Created($"/api/v1/transactions/{response.TransactionId}", response);
        })
        .AddEndpointFilter(new FeatureToggleFilter("transfers"))
        .AddEndpointFilter<IdempotencyFilter>()
        .RequireAuthorization()
        .WithName("ExecuteTransfer")
        .WithTags("Transfers")
        .Produces<TransferResponse>(201)
        .ProducesProblem(400)
        .ProducesProblem(422)
        .ProducesProblem(503);
    }
}
