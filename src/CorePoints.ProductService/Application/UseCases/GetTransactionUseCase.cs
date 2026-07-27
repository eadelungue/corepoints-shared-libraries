using System.Net.Http.Json;
using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Domain.Exceptions;
using CorePoints.Resilience.Clients;
using Microsoft.Extensions.Logging;

namespace CorePoints.ProductService.Application.UseCases;

public sealed class GetTransactionUseCase
{
    private readonly ILedgerClient _ledgerClient;
    private readonly ILogger<GetTransactionUseCase> _logger;

    public GetTransactionUseCase(ILedgerClient ledgerClient, ILogger<GetTransactionUseCase> logger)
    {
        _ledgerClient = ledgerClient;
        _logger = logger;
    }

    public async Task<TransactionDetailResponse> ExecuteAsync(
        Guid transactionId,
        string correlationId,
        CancellationToken ct)
    {
        var response = await _ledgerClient.GetTransactionAsync(
            transactionId.ToString(), correlationId, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new AccountNotFoundException($"Transaction '{transactionId}' was not found.");

        if (!response.IsSuccessStatusCode)
            LedgerResponseMapper.MapError(response);

        var txDetail = await response.Content
            .ReadFromJsonAsync<LedgerTransactionDetailResult>(cancellationToken: ct)
            ?? throw new LedgerUnavailableException("Failed to deserialize Ledger transaction response.");

        return new TransactionDetailResponse(
            txDetail.Id,
            txDetail.DebitAccountId,
            txDetail.CreditAccountId,
            txDetail.Amount,
            txDetail.Description,
            txDetail.CreatedAt);
    }
}
