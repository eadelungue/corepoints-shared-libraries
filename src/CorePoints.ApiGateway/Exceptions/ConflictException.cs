namespace CorePoints.ApiGateway.Exceptions;

/// <summary>
/// Thrown when a resource conflict occurs. Maps to HTTP 409 Conflict.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
    public ConflictException(string message, Exception innerException) : base(message, innerException) { }
}
