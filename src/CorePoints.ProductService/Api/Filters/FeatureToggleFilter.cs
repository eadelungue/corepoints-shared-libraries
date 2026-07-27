using CorePoints.FeatureToggles.Interfaces;

namespace CorePoints.ProductService.Api.Filters;

public sealed class FeatureToggleFilter : IEndpointFilter
{
    private readonly string _featureName;

    public FeatureToggleFilter(string featureName)
    {
        _featureName = featureName;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var featureToggles = context.HttpContext.RequestServices
            .GetRequiredService<IFeatureToggleService>();

        if (!await featureToggles.IsEnabledAsync(_featureName, cancellationToken: context.HttpContext.RequestAborted))
        {
            return Results.Problem(
                title: "Feature Unavailable",
                detail: $"The '{_featureName}' feature is currently disabled.",
                statusCode: 503);
        }

        return await next(context);
    }
}
