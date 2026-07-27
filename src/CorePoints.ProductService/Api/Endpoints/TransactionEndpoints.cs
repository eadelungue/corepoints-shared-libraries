using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Application.UseCases;

namespace CorePoints.ProductService.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/transactions/{id:guid}", async (
            Guid id,
            HttpContext ctx,
            GetTransactionUseCase useCase,
            CancellationToken ct) =>
        {
            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(id, correlationId, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization()
        .WithName("GetTransaction")
        .WithTags("Transactions")
        .Produces<TransactionDetailResponse>(200)
        .ProducesProblem(404);
    }
}
