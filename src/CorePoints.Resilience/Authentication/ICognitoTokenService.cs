namespace CorePoints.Resilience.Authentication;

/// <summary>
/// Abstraction for obtaining a machine-to-machine Cognito access token.
/// </summary>
public interface ICognitoTokenService
{
    /// <summary>
    /// Obtains a client credentials access token for the requested scope.
    /// </summary>
    /// <param name="scope">The OAuth2 scope to request, e.g. ledger:read or ledger:write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<string> GetTokenAsync(string scope, CancellationToken cancellationToken = default);
}
