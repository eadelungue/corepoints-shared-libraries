namespace CorePoints.LedgerCore.Application.Interfaces;

public interface IIdempotencyStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string responsePayload, CancellationToken ct);
}
