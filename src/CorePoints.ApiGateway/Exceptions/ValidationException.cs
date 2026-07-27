namespace CorePoints.ApiGateway.Exceptions;

/// <summary>
/// Thrown when request validation fails. Maps to HTTP 400 Bad Request.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
    public ValidationException(string message, Exception innerException) : base(message, innerException) { }
}
