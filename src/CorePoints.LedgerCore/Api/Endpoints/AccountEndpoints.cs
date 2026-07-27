using CorePoints.LedgerCore.Application.UseCases.CreateAccount;
using CorePoints.LedgerCore.Application.UseCases.GetBalance;
using CorePoints.LedgerCore.Application.UseCases.GetStatement;

namespace CorePoints.LedgerCore.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts", async (
            CreateAccountRequest request,
            CreateAccountUseCase useCase,
            CancellationToken ct) =>
        {
            var response = await useCase.ExecuteAsync(request, ct);
            return Results.Created($"/accounts/{response.Id}", response);
        })
        .WithName("CreateAccount")
        .Produces<AccountResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/accounts/{id:guid}/balance", async (
            Guid id,
            GetBalanceUseCase useCase,
            CancellationToken ct) =>
        {
            var response = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(response);
        })
        .WithName("GetAccountBalance")
        .Produces<BalanceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/accounts/{id:guid}/statement", async (
            Guid id,
            [AsParameters] StatementQueryParams queryParams,
            GetStatementUseCase useCase,
            CancellationToken ct) =>
        {
            var request = new StatementRequest(id, queryParams.Page ?? 1, queryParams.PageSize ?? 20);
            var response = await useCase.ExecuteAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetAccountStatement")
        .Produces<PaginatedStatementResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

public sealed record StatementQueryParams(int? Page, int? PageSize);
