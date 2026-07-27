using System.Net.Http.Json;
using CorePoints.Caching.Abstractions;
using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Domain.Exceptions;
using CorePoints.Resilience.Clients;
using Microsoft.Extensions.Logging;

namespace CorePoints.ProductService.Application.UseCases;

public sealed class GetBalanceUseCase
{
    private readonly ILedgerClient _ledgerClient;
    private readonly ICacheService _cacheService;
    private readonly ILogger<GetBalanceUseCase> _logger;

    private static readonly TimeSpan BalanceCacheTtl = TimeSpan.FromSeconds(5);

    public GetBalanceUseCase(
        ILedgerClient ledgerClient,
        ICacheService cacheService,
        ILogger<GetBalanceUseCase> logger)
    {
        _ledgerClient = ledgerClient;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<BalanceResponse> ExecuteAsync(
        Guid accountId,
        string correlationId,
        CancellationToken ct)
    {
        // 1. Check cache (5s TTL)
        var cacheKey = $"product:balance:{accountId}";
        var cached = await _cacheService.GetAsync<BalanceResponse>(cacheKey, ct);
        if (cached is not null) return cached;

        // 2. Proxy to Ledger
        var response = await _ledgerClient.GetBalanceAsync(
            accountId.ToString(), correlationId, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new AccountNotFoundException(accountId);

        if (!response.IsSuccessStatusCode)
            LedgerResponseMapper.MapError(response);

        var ledgerBalance = await response.Content
            .ReadFromJsonAsync<LedgerBalanceResult>(cancellationToken: ct)
            ?? throw new LedgerUnavailableException("Failed to deserialize Ledger balance response.");

        // 3. Map to Product DTO and cache
        var result = new BalanceResponse(accountId, ledgerBalance.Balance);
        await _cacheService.SetAsync(cacheKey, result, BalanceCacheTtl, ct);

        _logger.LogInformation(
            "Balance retrieved for Account={AccountId}, Balance={Balance}",
            accountId, result.Balance);

        return result;
    }
}
