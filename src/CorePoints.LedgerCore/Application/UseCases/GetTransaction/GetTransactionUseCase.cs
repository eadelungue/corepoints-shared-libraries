using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Application.UseCases.RecordTransaction;
using CorePoints.LedgerCore.Domain.Exceptions;

namespace CorePoints.LedgerCore.Application.UseCases.GetTransaction;

public sealed class GetTransactionUseCase(ITransactionRepository transactionRepository)
{
    public async Task<TransactionResponse> ExecuteAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, ct)
            ?? throw new TransactionNotFoundException(transactionId);

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
