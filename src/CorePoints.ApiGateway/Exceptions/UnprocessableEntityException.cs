namespace CorePoints.ApiGateway.Exceptions;

/// <summary>
/// Thrown when a request is syntactically valid but semantically incorrect. Maps to HTTP 422 Unprocessable Entity.
/// </summary>
public class UnprocessableEntityException : Exception
{
    public UnprocessableEntityException(string message) : base(message) { }
    public UnprocessableEntityException(string message, Exception innerException) : base(message, innerException) { }
}
