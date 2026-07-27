namespace CorePoints.ProductService.Domain.Exceptions;

public sealed class LedgerUnavailableException : Exception
{
    public LedgerUnavailableException(string message = "Ledger service is unavailable.")
        : base(message) { }
}
