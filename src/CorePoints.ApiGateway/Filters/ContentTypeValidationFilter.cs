using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace CorePoints.ApiGateway.Filters;

/// <summary>
/// Action filter that rejects non-JSON request bodies on POST and PUT methods with HTTP 415 Unsupported Media Type.
/// Returns an RFC 7807 ProblemDetails response.
/// </summary>
public class ContentTypeValidationFilter : IActionFilter
{
    private static readonly HashSet<string> MethodsRequiringJson = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put
    };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;

        if (!MethodsRequiringJson.Contains(request.Method))
        {
            return;
        }

        // Only validate if there's a body (Content-Length > 0 or Transfer-Encoding is set)
        if (request.ContentLength is null or 0 && !request.Headers.ContainsKey("Transfer-Encoding"))
        {
            return;
        }

        var contentType = request.ContentType;

        if (string.IsNullOrEmpty(contentType) || !IsJsonContentType(contentType))
        {
            var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status415UnsupportedMediaType,
                Title = "Unsupported Media Type",
                Detail = "Request body must have Content-Type: application/json.",
                Instance = request.Path
            };
            problem.Extensions["correlationId"] = correlationId;

            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status415UnsupportedMediaType,
                ContentTypes = { "application/problem+json" }
            };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // No post-processing needed
    }

    private static bool IsJsonContentType(string contentType)
    {
        if (MediaTypeHeaderValue.TryParse(contentType, out var parsedValue))
        {
            return parsedValue.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
