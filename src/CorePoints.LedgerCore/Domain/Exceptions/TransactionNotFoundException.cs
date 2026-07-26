namespace CorePoints.LedgerCore.Domain.Exceptions;

public sealed class TransactionNotFoundException : Exception
{
    public Guid TransactionId { get; }

    public TransactionNotFoundException(Guid transactionId)
        : base($"Transaction {transactionId} was not found.")
    {
        TransactionId = transactionId;
    }
}
