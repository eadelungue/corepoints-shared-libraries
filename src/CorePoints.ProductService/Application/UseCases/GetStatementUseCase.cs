using System.Net.Http.Json;
using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Domain.Exceptions;
using CorePoints.Resilience.Clients;
using Microsoft.Extensions.Logging;

namespace CorePoints.ProductService.Application.UseCases;

public sealed class GetStatementUseCase
{
    private readonly ILedgerClient _ledgerClient;
    private readonly ILogger<GetStatementUseCase> _logger;

    public GetStatementUseCase(ILedgerClient ledgerClient, ILogger<GetStatementUseCase> logger)
    {
        _ledgerClient = ledgerClient;
        _logger = logger;
    }

    public async Task<StatementResponse> ExecuteAsync(
        Guid accountId,
        int page,
        int pageSize,
        string correlationId,
        CancellationToken ct)
    {
        var response = await _ledgerClient.GetStatementAsync(
            accountId.ToString(), correlationId, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new AccountNotFoundException(accountId);

        if (!response.IsSuccessStatusCode)
            LedgerResponseMapper.MapError(response);

        var ledgerStatement = await response.Content
            .ReadFromJsonAsync<LedgerStatementResult>(cancellationToken: ct)
            ?? throw new LedgerUnavailableException("Failed to deserialize Ledger statement response.");

        return LedgerResponseMapper.ToStatementResponse(ledgerStatement);
    }
}
