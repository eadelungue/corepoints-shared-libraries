using CorePoints.Caching.Extensions;
using CorePoints.FeatureToggles.Extensions;
using CorePoints.ProductService.Api.Endpoints;
using CorePoints.ProductService.Api.Middleware;
using CorePoints.ProductService.Application.Interfaces;
using CorePoints.ProductService.Application.UseCases;
using CorePoints.ProductService.Infrastructure.Data;
using CorePoints.ProductService.Infrastructure.Idempotency;
using CorePoints.Resilience.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.WithProperty("ServiceName", "ProductService")
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Database
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

// Redis + Caching (CorePoints.Caching — registers IConnectionMultiplexer, ICacheService, ProductDataCacheService)
builder.Services.AddCorePointsCaching(builder.Configuration);

// Feature Toggles (CorePoints.FeatureToggles)
builder.Services.AddFeatureToggles(_ => { });
builder.Services.Configure<CorePoints.FeatureToggles.Models.FeatureToggleOptions>(
    builder.Configuration.GetSection("FeatureToggles"));

// Ledger Client (CorePoints.Resilience — includes Polly policies, ILedgerClient)
builder.Services.AddCorePointsResilience(builder.Configuration);

// Repositories
builder.Services.AddScoped<ICashbackRuleRepository, CashbackRuleRepository>();
builder.Services.AddScoped<ITransferLimitRepository, TransferLimitRepository>();
builder.Services.AddScoped<ITransferHistoryRepository, TransferHistoryRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();

// Use Cases
builder.Services.AddScoped<CreditCashbackUseCase>();
builder.Services.AddScoped<ExecuteTransferUseCase>();
builder.Services.AddScoped<GetBalanceUseCase>();
builder.Services.AddScoped<GetStatementUseCase>();
builder.Services.AddScoped<GetTransactionUseCase>();

// JWT Auth
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.Audience = builder.Configuration["Jwt:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });
builder.Services.AddAuthorization();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ProductService"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Kestrel port 8080
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Endpoints
app.MapCashbackEndpoints();
app.MapTransferEndpoints();
app.MapAccountEndpoints();
app.MapTransactionEndpoints();
app.MapHealthEndpoints();

app.Run();
