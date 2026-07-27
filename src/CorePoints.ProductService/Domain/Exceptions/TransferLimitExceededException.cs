namespace CorePoints.ProductService.Domain.Exceptions;

public sealed class TransferLimitExceededException : Exception
{
    public TransferLimitExceededException(string message) : base(message) { }
}
