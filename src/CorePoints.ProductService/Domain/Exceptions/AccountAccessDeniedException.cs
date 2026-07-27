namespace CorePoints.ProductService.Domain.Exceptions;

public sealed class AccountAccessDeniedException : Exception
{
    public AccountAccessDeniedException(string message = "Access to this account is denied.")
        : base(message) { }
}
