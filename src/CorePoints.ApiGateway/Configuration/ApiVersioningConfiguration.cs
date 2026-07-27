using Asp.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace CorePoints.ApiGateway.Configuration;

/// <summary>
/// Configures API versioning using URL segment reader for /api/v{version}/ pattern.
/// </summary>
public static class ApiVersioningConfiguration
{
    public static IServiceCollection AddApiVersioningConfig(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}
