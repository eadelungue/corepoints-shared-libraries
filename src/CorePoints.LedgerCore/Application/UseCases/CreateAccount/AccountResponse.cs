using CorePoints.LedgerCore.Domain.ValueObjects;

namespace CorePoints.LedgerCore.Application.UseCases.CreateAccount;

public sealed record AccountResponse(
    Guid Id,
    string HolderName,
    AccountType AccountType,
    decimal Balance,
    DateTime CreatedAt);
