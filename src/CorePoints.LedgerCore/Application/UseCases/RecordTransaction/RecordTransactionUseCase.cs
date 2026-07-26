using System.Text.Json;
using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Domain.Entities;
using CorePoints.LedgerCore.Domain.Exceptions;

namespace CorePoints.LedgerCore.Application.UseCases.RecordTransaction;

public sealed class RecordTransactionUseCase(
    IAccountRepository accountRepository,
    ITransactionRepository transactionRepository,
    IOutboxRepository outboxRepository,
    IBalanceCacheService balanceCache,
    IDbConnectionFactory connectionFactory)
{
    public async Task<TransactionResponse> ExecuteAsync(
        CreateTransactionRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken ct)
    {
        // 1. Open connection + begin transaction
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        using var tx = await connection.BeginTransactionAsync(ct);

        // 2. Lock both accounts with SELECT ... FOR UPDATE
        var debitAccount = await accountRepository.GetForUpdateAsync(request.DebitAccountId, connection, tx, ct)
            ?? throw new AccountNotFoundException(request.DebitAccountId);

        var creditAccount = await accountRepository.GetForUpdateAsync(request.CreditAccountId, connection, tx, ct)
            ?? throw new AccountNotFoundException(request.CreditAccountId);

        // 3. Domain logic — debit and credit
        debitAccount.Debit(request.Amount);
        creditAccount.Credit(request.Amount);

        // 4. Persist transaction record
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            DebitAccountId = request.DebitAccountId,
            CreditAccountId = request.CreditAccountId,
            Amount = request.Amount,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        await transactionRepository.InsertAsync(transaction, connection, tx, ct);

        // 5. Update account balances
        await accountRepository.UpdateBalanceAsync(debitAccount, connection, tx, ct);
        await accountRepository.UpdateBalanceAsync(creditAccount, connection, tx, ct);

        // 6. Persist outbox event in SAME transaction
        var outboxEvent = new OutboxEvent
        {
            Id = Guid.NewGuid(),
            EventType = "TransactionRecorded",
            Payload = JsonSerializer.Serialize(new
            {
                TransactionId = transaction.Id,
                transaction.DebitAccountId,
                transaction.CreditAccountId,
                transaction.Amount,
                CorrelationId = correlationId
            }),
            CorrelationId = correlationId,
            CreatedAt = DateTime.UtcNow
        };

        await outboxRepository.InsertAsync(outboxEvent, connection, tx, ct);

        // 7. Commit ACID transaction
        await tx.CommitAsync(ct);

        // 8. Invalidate balance cache synchronously (post-commit)
        await balanceCache.InvalidateAsync(request.DebitAccountId, ct);
        await balanceCache.InvalidateAsync(request.CreditAccountId, ct);

        return new TransactionResponse(
            transaction.Id,
            transaction.IdempotencyKey,
            transaction.DebitAccountId,
            transaction.CreditAccountId,
            transaction.Amount,
            transaction.Description,
            transaction.CreatedAt);
    }
}
