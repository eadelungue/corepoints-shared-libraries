using CorePoints.LedgerCore.Domain.ValueObjects;

namespace CorePoints.LedgerCore.Application.UseCases.CreateAccount;

public sealed record CreateAccountRequest(string HolderName, AccountType AccountType);
