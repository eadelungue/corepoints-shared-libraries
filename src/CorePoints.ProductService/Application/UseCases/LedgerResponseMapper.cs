using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Domain.Exceptions;

namespace CorePoints.ProductService.Application.UseCases;

public static class LedgerResponseMapper
{
    public static void MapError(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;

        if (statusCode == 422)
            throw new InsufficientBalanceException();
        if (statusCode == 404)
            throw new AccountNotFoundException();

        throw new LedgerUnavailableException(
            $"Ledger returned unexpected status code: {statusCode}");
    }

    public static StatementResponse ToStatementResponse(LedgerStatementResult ledger)
        => new(
            ledger.Items.Select(i => new StatementItem(
                i.Id, i.Amount, i.Description, i.CreatedAt)).ToList(),
            ledger.Page,
            ledger.PageSize,
            ledger.TotalCount);
}
