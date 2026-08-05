using System.Net.Http.Headers;
using System.Net.Http.Json;
using CorePoints.Resilience.Authentication;
using Microsoft.Extensions.Logging;

namespace CorePoints.Resilience.Clients;

/// <summary>
/// Typed Ledger client that resolves the "LedgerClient" named HttpClient from IHttpClientFactory.
/// Attaches Idempotency-Key, X-Correlation-ID and Authorization headers on every request.
/// Propagates CancellationToken through all async calls.
/// </summary>
public sealed class LedgerHttpClient : ILedgerClient
{
    private readonly HttpClient _httpClient;
    private readonly ICognitoTokenService _cognitoTokenService;
    private readonly ILogger<LedgerHttpClient> _logger;

    public LedgerHttpClient(
        IHttpClientFactory httpClientFactory,
        ICognitoTokenService cognitoTokenService,
        ILogger<LedgerHttpClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("LedgerClient");
        _cognitoTokenService = cognitoTokenService;
        _logger = logger;
    }

    public async Task<HttpResponseMessage> PostTransactionAsync(
        object request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(request)
        };
        await AttachHeadersAsync(httpRequest, idempotencyKey, correlationId, "ledger:write", cancellationToken);

        _logger.LogDebug(
            "Posting transaction to Ledger. IdempotencyKey={IdempotencyKey}, CorrelationId={CorrelationId}",
            idempotencyKey, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetBalanceAsync(
        string accountId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/accounts/{accountId}/balance");
        await AttachHeadersAsync(httpRequest, idempotencyKey: null, correlationId, "ledger:read", cancellationToken);

        _logger.LogDebug(
            "Getting balance from Ledger. AccountId={AccountId}, CorrelationId={CorrelationId}",
            accountId, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetStatementAsync(
        string accountId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/accounts/{accountId}/statement");
        await AttachHeadersAsync(httpRequest, idempotencyKey: null, correlationId, "ledger:read", cancellationToken);

        _logger.LogDebug(
            "Getting statement from Ledger. AccountId={AccountId}, CorrelationId={CorrelationId}",
            accountId, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetTransactionAsync(
        string transactionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/transactions/{transactionId}");
        await AttachHeadersAsync(httpRequest, idempotencyKey: null, correlationId, "ledger:read", cancellationToken);

        _logger.LogDebug(
            "Getting transaction from Ledger. TransactionId={TransactionId}, CorrelationId={CorrelationId}",
            transactionId, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetTitularByDocumentoAsync(
        string documento,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/titulares?documento={Uri.EscapeDataString(documento)}");
        await AttachHeadersAsync(httpRequest, idempotencyKey: null, correlationId, "ledger:read", cancellationToken);

        _logger.LogDebug(
            "Getting titular by documento from Ledger. CorrelationId={CorrelationId}",
            correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> PostTitularAsync(
        object request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/titulares")
        {
            Content = JsonContent.Create(request)
        };
        await AttachHeadersAsync(httpRequest, idempotencyKey: null, correlationId, "ledger:write", cancellationToken);

        _logger.LogDebug(
            "Posting titular to Ledger. CorrelationId={CorrelationId}",
            correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> PostContaAsync(
        object request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/contas")
        {
            Content = JsonContent.Create(request)
        };
        await AttachHeadersAsync(httpRequest, idempotencyKey: null, correlationId, "ledger:write", cancellationToken);

        _logger.LogDebug(
            "Posting conta to Ledger. CorrelationId={CorrelationId}",
            correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    private async Task AttachHeadersAsync(
        HttpRequestMessage request,
        string? idempotencyKey,
        string correlationId,
        string scope,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        request.Headers.Add("X-Correlation-ID", correlationId);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await _cognitoTokenService.GetTokenAsync(scope, cancellationToken));
    }
}
