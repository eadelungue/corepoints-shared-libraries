using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Application.UseCases;

namespace CorePoints.ProductService.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/accounts/{id:guid}/balance", async (
            Guid id,
            HttpContext ctx,
            GetBalanceUseCase useCase,
            CancellationToken ct) =>
        {
            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(id, correlationId, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization()
        .WithName("GetBalance")
        .WithTags("Accounts")
        .Produces<BalanceResponse>(200)
        .ProducesProblem(403)
        .ProducesProblem(404);

        app.MapGet("/api/v1/accounts/{id:guid}/statement", async (
            Guid id,
            int? page,
            int? pageSize,
            HttpContext ctx,
            GetStatementUseCase useCase,
            CancellationToken ct) =>
        {
            var p = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 20, 1, 100);

            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(id, p, ps, correlationId, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization()
        .WithName("GetStatement")
        .WithTags("Accounts")
        .Produces<StatementResponse>(200)
        .ProducesProblem(403)
        .ProducesProblem(404);
    }
}
