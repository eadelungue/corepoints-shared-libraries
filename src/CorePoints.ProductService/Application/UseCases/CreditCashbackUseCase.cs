using System.Net.Http.Json;
using System.Text.Json;
using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Domain.Exceptions;
using CorePoints.ProductService.Domain.Entities;
using CorePoints.ProductService.Domain.Services;
using CorePoints.Resilience.Clients;
using Microsoft.Extensions.Logging;

namespace CorePoints.ProductService.Application.UseCases;

public sealed class CreditCashbackUseCase
{
    private readonly ICashbackRuleRepository _cashbackRuleRepo;
    private readonly ILedgerClient _ledgerClient;
    private readonly IOutboxRepository _outboxRepo;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<CreditCashbackUseCase> _logger;

    public CreditCashbackUseCase(
        ICashbackRuleRepository cashbackRuleRepo,
        ILedgerClient ledgerClient,
        IOutboxRepository outboxRepo,
        IDbConnectionFactory connectionFactory,
        ILogger<CreditCashbackUseCase> logger)
    {
        _cashbackRuleRepo = cashbackRuleRepo;
        _ledgerClient = ledgerClient;
        _outboxRepo = outboxRepo;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<CashbackResponse> ExecuteAsync(
        CreditCashbackRequest request,
        string correlationId,
        CancellationToken ct)
    {
        // 1. Load active cashback rule for the account group
        var rule = await _cashbackRuleRepo.GetActiveRuleAsync(request.AccountGroup, ct)
            ?? throw new IneligibleCashbackException("No active cashback rule found for this account group.");

        // 2. Validate eligibility
        if (!CashbackCalculator.IsEligible(request.AccountGroup, rule))
            throw new IneligibleCashbackException("Account group is not eligible for cashback.");

        // 3. Calculate cashback (decimal only)
        var cashbackAmount = CashbackCalculator.Calculate(request.TransactionAmount, rule);

        _logger.LogInformation(
            "Cashback calculated: Amount={TransactionAmount}, Rule={RuleName}, Percentage={Percentage}, CashbackAmount={CashbackAmount}",
            request.TransactionAmount, rule.Name, rule.Percentage, cashbackAmount);

        // 4. Generate idempotency key for Ledger call (isolated from client key)
        var ledgerIdempotencyKey = Guid.NewGuid().ToString();

        // 5. Call Ledger to credit cashback
        var ledgerResponse = await _ledgerClient.PostTransactionAsync(
            new
            {
                DebitAccountId = request.SystemSourceAccountId,
                CreditAccountId = request.UserAccountId,
                Amount = cashbackAmount,
                Description = $"Cashback: {rule.Name}"
            },
            ledgerIdempotencyKey,
            correlationId,
            ct);

        if (!ledgerResponse.IsSuccessStatusCode)
            LedgerResponseMapper.MapError(ledgerResponse);

        var txResult = await ledgerResponse.Content
            .ReadFromJsonAsync<LedgerTransactionResult>(cancellationToken: ct)
            ?? throw new LedgerUnavailableException("Failed to deserialize Ledger response.");

        _logger.LogInformation(
            "Ledger transaction completed: TransactionId={TransactionId}, CashbackAmount={CashbackAmount}",
            txResult.TransactionId, cashbackAmount);

        // 6. Persist outbox event in Product DB
        using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await _outboxRepo.InsertAsync(new OutboxEvent
        {
            EventType = "CashbackCredited",
            Payload = JsonSerializer.Serialize(new
            {
                txResult.TransactionId,
                request.UserAccountId,
                CashbackAmount = cashbackAmount,
                request.OriginalTransactionRef
            }),
            CorrelationId = correlationId
        }, conn, ct);

        return new CashbackResponse(txResult.TransactionId, cashbackAmount);
    }
}
