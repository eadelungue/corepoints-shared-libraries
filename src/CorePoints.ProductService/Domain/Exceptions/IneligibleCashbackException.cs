namespace CorePoints.ProductService.Domain.Exceptions;

public sealed class IneligibleCashbackException : Exception
{
    public IneligibleCashbackException(string message) : base(message) { }
}
