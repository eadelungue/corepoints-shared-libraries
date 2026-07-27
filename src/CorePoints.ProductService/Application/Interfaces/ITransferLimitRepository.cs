using CorePoints.ProductService.Domain.Entities;

namespace CorePoints.ProductService.Application.Interfaces;

public interface ITransferLimitRepository
{
    Task<TransferLimit?> GetByAccountTypeAsync(string accountType, CancellationToken ct = default);
}
