namespace CorePoints.ProductService.Application.Interfaces;

public interface IIdempotencyStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string responsePayload, CancellationToken ct = default);
}
