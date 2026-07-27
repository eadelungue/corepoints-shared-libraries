namespace CorePoints.ProductService.Domain.Exceptions;

public sealed class AccountNotFoundException : Exception
{
    public Guid? AccountId { get; }

    public AccountNotFoundException(Guid accountId)
        : base($"Account '{accountId}' was not found.") => AccountId = accountId;

    public AccountNotFoundException(string message = "Account not found.")
        : base(message) { }
}
