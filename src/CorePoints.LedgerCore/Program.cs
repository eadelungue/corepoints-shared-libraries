using CorePoints.LedgerCore.Api.Endpoints;
using CorePoints.LedgerCore.Api.Middleware;
using CorePoints.LedgerCore.Application.Interfaces;
using CorePoints.LedgerCore.Application.UseCases.CreateAccount;
using CorePoints.LedgerCore.Application.UseCases.GetBalance;
using CorePoints.LedgerCore.Application.UseCases.GetStatement;
using CorePoints.LedgerCore.Application.UseCases.GetTransaction;
using CorePoints.LedgerCore.Application.UseCases.RecordTransaction;
using CorePoints.LedgerCore.Infrastructure.Cache;
using CorePoints.LedgerCore.Infrastructure.Data;
using CorePoints.LedgerCore.Infrastructure.Idempotency;
using CorePoints.LedgerCore.Infrastructure.Resilience;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.WithProperty("ServiceName", "LedgerCore")
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

// Kestrel port 8080
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

// Database
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

// Repositories
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();

// Services
builder.Services.AddScoped<IBalanceCacheService, RedisBalanceCacheService>();
builder.Services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();

// Use Cases
builder.Services.AddScoped<CreateAccountUseCase>();
builder.Services.AddScoped<RecordTransactionUseCase>();
builder.Services.AddScoped<GetBalanceUseCase>();
builder.Services.AddScoped<GetStatementUseCase>();
builder.Services.AddScoped<GetTransactionUseCase>();

// Polly resilience pipelines
builder.Services.AddResiliencePipelines();

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map endpoints
app.MapAccountEndpoints();
app.MapTransactionEndpoints();
app.MapHealthEndpoints();

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
