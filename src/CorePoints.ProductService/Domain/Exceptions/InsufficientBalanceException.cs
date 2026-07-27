namespace CorePoints.ProductService.Domain.Exceptions;

public sealed class InsufficientBalanceException : Exception
{
    public InsufficientBalanceException(string message = "Insufficient balance to complete the operation.")
        : base(message) { }
}
