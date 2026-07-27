using System.Net.Http.Json;
using System.Text.Json;
using CorePoints.ProductService.Application.DTOs;
using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Domain.Entities;
using CorePoints.ProductService.Domain.Exceptions;
using CorePoints.ProductService.Domain.Services;
using CorePoints.Resilience.Clients;
using Microsoft.Extensions.Logging;

namespace CorePoints.ProductService.Application.UseCases;

public sealed class ExecuteTransferUseCase
{
    private readonly ITransferLimitRepository _limitRepo;
    private readonly ITransferHistoryRepository _historyRepo;
    private readonly ILedgerClient _ledgerClient;
    private readonly IOutboxRepository _outboxRepo;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<ExecuteTransferUseCase> _logger;

    public ExecuteTransferUseCase(
        ITransferLimitRepository limitRepo,
        ITransferHistoryRepository historyRepo,
        ILedgerClient ledgerClient,
        IOutboxRepository outboxRepo,
        IDbConnectionFactory connectionFactory,
        ILogger<ExecuteTransferUseCase> logger)
    {
        _limitRepo = limitRepo;
        _historyRepo = historyRepo;
        _ledgerClient = ledgerClient;
        _outboxRepo = outboxRepo;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<TransferResponse> ExecuteAsync(
        TransferRequest request,
        string correlationId,
        CancellationToken ct)
    {
        // 1. Load transfer limits for source account type
        var limit = await _limitRepo.GetByAccountTypeAsync(request.SourceAccountType, ct)
            ?? throw new TransferLimitExceededException("No transfer limit configuration found for this account type.");

        // 2. Get today's transfer total for source account
        var dailyTotal = await _historyRepo
            .GetDailyTotalAsync(request.SourceAccountId, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        // 3. Validate limits (domain service)
        TransferValidator.Validate(request.Amount, dailyTotal, limit);

        _logger.LogInformation(
            "Transfer validated: SourceAccount={SourceAccountId}, Amount={Amount}, DailyTotal={DailyTotal}, DailyLimit={DailyLimit}",
            request.SourceAccountId, request.Amount, dailyTotal, limit.DailyLimit);

        // 4. Generate idempotency key for Ledger
        var ledgerIdempotencyKey = Guid.NewGuid().ToString();

        // 5. Call Ledger
        var ledgerResponse = await _ledgerClient.PostTransactionAsync(
            new
            {
                DebitAccountId = request.SourceAccountId,
                CreditAccountId = request.DestinationAccountId,
                Amount = request.Amount,
                Description = $"Transfer: {request.SourceAccountId} → {request.DestinationAccountId}"
            },
            ledgerIdempotencyKey,
            correlationId,
            ct);

        // 6. Handle Ledger errors (422 = insufficient balance)
        if (!ledgerResponse.IsSuccessStatusCode)
            LedgerResponseMapper.MapError(ledgerResponse);

        var txResult = await ledgerResponse.Content
            .ReadFromJsonAsync<LedgerTransactionResult>(cancellationToken: ct)
            ?? throw new LedgerUnavailableException("Failed to deserialize Ledger response.");

        _logger.LogInformation(
            "Ledger transfer completed: TransactionId={TransactionId}, Amount={Amount}",
            txResult.TransactionId, request.Amount);

        // 7. Persist transfer history + outbox event
        using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);

        await _historyRepo.InsertAsync(new TransferHistoryEntry(
            Guid.NewGuid(),
            request.SourceAccountId,
            request.DestinationAccountId,
            request.Amount,
            txResult.TransactionId,
            DateTime.UtcNow), conn, ct);

        await _outboxRepo.InsertAsync(new OutboxEvent
        {
            EventType = "TransferCompleted",
            Payload = JsonSerializer.Serialize(new
            {
                txResult.TransactionId,
                request.SourceAccountId,
                request.DestinationAccountId,
                request.Amount
            }),
            CorrelationId = correlationId
        }, conn, ct);

        return new TransferResponse(txResult.TransactionId, request.Amount);
    }
}
