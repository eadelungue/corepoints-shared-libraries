using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Domain.Entities;

namespace CorePoints.LedgerCore.Application.UseCases.CreateAccount;

public sealed class CreateAccountUseCase(IAccountRepository accountRepository)
{
    public async Task<AccountResponse> ExecuteAsync(CreateAccountRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.HolderName))
            throw new ArgumentException("HolderName is required.");

        var account = new Account
        {
            Id = Guid.NewGuid(),
            HolderName = request.HolderName,
            AccountType = request.AccountType,
            Balance = 0m,
            CreatedAt = DateTime.UtcNow
        };

        await accountRepository.InsertAsync(account, ct);

        return new AccountResponse(
            account.Id,
            account.HolderName,
            account.AccountType,
            account.Balance,
            account.CreatedAt);
    }
}
