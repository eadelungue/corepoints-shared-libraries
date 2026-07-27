namespace CorePoints.ApiGateway.Exceptions;

/// <summary>
/// Thrown when a requested resource is not found. Maps to HTTP 404 Not Found.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
