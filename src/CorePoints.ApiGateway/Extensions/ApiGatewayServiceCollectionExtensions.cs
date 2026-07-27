using CorePoints.ApiGateway.Configuration;
using CorePoints.ApiGateway.Filters;
using CorePoints.ApiGateway.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CorePoints.ApiGateway.Extensions;

/// <summary>
/// Extension methods for registering all API Gateway BFF services and configuring the middleware pipeline.
/// </summary>
public static class ApiGatewayServiceCollectionExtensions
{
    /// <summary>
    /// Registers all API Gateway services: ICorrelationIdAccessor, API versioning, Swagger, and content-type filter.
    /// </summary>
    public static IServiceCollection AddApiGatewayServices(this IServiceCollection services, IConfiguration configuration)
    {
        // HTTP context accessor (required for correlation ID)
        services.AddHttpContextAccessor();

        // Correlation ID accessor via DI
        services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();

        // Correlation ID delegating handler for outgoing HTTP calls
        services.AddTransient<CorrelationIdDelegatingHandler>();

        // API versioning
        services.AddApiVersioningConfig();

        // Swagger with JWT security definition
        services.AddSwaggerWithJwt(configuration);

        // Register content-type validation filter globally
        services.AddControllers(options =>
        {
            options.Filters.Add<ContentTypeValidationFilter>();
        });

        return services;
    }

    /// <summary>
    /// Configures the API Gateway middleware pipeline in the correct order:
    /// CorrelationId → ErrorHandling → Swagger (non-prod) → Routing → Auth → Controllers
    /// </summary>
    public static WebApplication UseApiGatewayPipeline(this WebApplication app)
    {
        // 1. Correlation ID (first, so all downstream middleware/logs have it)
        app.UseMiddleware<CorrelationIdMiddleware>();

        // 2. Error handling (wraps everything to catch exceptions from any layer)
        app.UseMiddleware<GlobalExceptionMiddleware>();

        // 3. Swagger UI (non-production only)
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // 4. Routing
        app.UseRouting();

        // 5. Authentication & Authorization
        app.UseAuthentication();
        app.UseAuthorization();

        // 6. Map controllers
        app.MapControllers();

        return app;
    }
}
