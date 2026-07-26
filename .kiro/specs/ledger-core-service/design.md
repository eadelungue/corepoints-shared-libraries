# Design Document

## Overview

The Ledger Core Service follows Clean Architecture with four layers: Domain, Application, Infrastructure, and API. It is a C# .NET 8 Minimal API application using Dapper for PostgreSQL access, Redis for caching/idempotency, Serilog for structured logging, and Polly for resilience. The application runs on ECS Fargate at port 8080, discoverable via AWS Cloud Map.

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   API Layer                          │
│  (Minimal API Endpoints, Middleware, Filters)       │
├─────────────────────────────────────────────────────┤
│               Application Layer                     │
│  (Use Cases: CreateAccount, RecordTransaction,      │
│   GetBalance, GetStatement, GetTransaction)         │
├─────────────────────────────────────────────────────┤
│              Domain Layer                           │
│  (Entities: Account, Transaction, OutboxEvent)      │
│  (Value Objects: Money, AccountType)                │
├─────────────────────────────────────────────────────┤
│            Infrastructure Layer                     │
│  (Repositories, Redis Cache, Idempotency Store,     │
│   DbConnectionFactory, Polly Pipelines)             │
└─────────────────────────────────────────────────────┘
```

## Project Structure

```
src/CorePoints.LedgerCore/
├── Program.cs
├── Domain/
│   ├── Entities/
│   │   ├── Account.cs
│   │   ├── Transaction.cs
│   │   └── OutboxEvent.cs
│   ├── ValueObjects/
│   │   ├── Money.cs
│   │   └── AccountType.cs
│   └── Exceptions/
│       ├── InsufficientBalanceException.cs
│       └── AccountNotFoundException.cs
├── Application/
│   ├── UseCases/
│   │   ├── CreateAccount/
│   │   │   ├── CreateAccountUseCase.cs
│   │   │   ├── CreateAccountRequest.cs
│   │   │   └── CreateAccountResponse.cs
│   │   ├── RecordTransaction/
│   │   │   ├── RecordTransactionUseCase.cs
│   │   │   ├── CreateTransactionRequest.cs
│   │   │   └── TransactionResponse.cs
│   │   ├── GetBalance/
│   │   │   ├── GetBalanceUseCase.cs
│   │   │   └── BalanceResponse.cs
│   │   ├── GetStatement/
│   │   │   ├── GetStatementUseCase.cs
│   │   │   ├── StatementRequest.cs
│   │   │   └── PaginatedStatementResponse.cs
│   │   └── GetTransaction/
│   │       ├── GetTransactionUseCase.cs
│   │       └── TransactionResponse.cs
│   └── Interfaces/
│       ├── IAccountRepository.cs
│       ├── ITransactionRepository.cs
│       ├── IOutboxRepository.cs
│       ├── IIdempotencyStore.cs
│       └── IBalanceCacheService.cs
├── Infrastructure/
│   ├── Data/
│   │   ├── DbConnectionFactory.cs
│   │   ├── AccountRepository.cs
│   │   ├── TransactionRepository.cs
│   │   └── OutboxRepository.cs
│   ├── Cache/
│   │   └── RedisBalanceCacheService.cs
│   ├── Idempotency/
│   │   └── RedisIdempotencyStore.cs
│   └── Resilience/
│       └── PollyPipelineConfiguration.cs
├── Api/
│   ├── Endpoints/
│   │   ├── AccountEndpoints.cs
│   │   ├── TransactionEndpoints.cs
│   │   └── HealthEndpoints.cs
│   ├── Middleware/
│   │   ├── CorrelationIdMiddleware.cs
│   │   └── GlobalExceptionMiddleware.cs
│   └── Filters/
│       └── IdempotencyFilter.cs
└── Configuration/
    ├── ServiceRegistration.cs
    ├── DatabaseConfiguration.cs
    ├── RedisConfiguration.cs
    └── ObservabilityConfiguration.cs
```

## Component Design

### 1. Domain Layer

#### Account Entity

```csharp
public sealed class Account
{
    public Guid Id { get; init; }
    public string HolderName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public decimal Balance { get; private set; }
    public DateTime CreatedAt { get; init; }

    public void Debit(decimal amount)
    {
        if (Balance < amount)
            throw new InsufficientBalanceException(Id, Balance, amount);
        Balance -= amount;
    }

    public void Credit(decimal amount) => Balance += amount;
}
```

#### AccountType Value Object

```csharp
public enum AccountType
{
    DEBIT,
    CREDIT
}
```

#### Money is handled as `decimal` throughout — no dedicated value object wrapper needed since the DB uses DECIMAL(18,4) and C# decimal maps directly.

### 2. Application Layer — Use Cases

#### RecordTransactionUseCase (core flow)

```csharp
public sealed class RecordTransactionUseCase(
    IAccountRepository accountRepository,
    ITransactionRepository transactionRepository,
    IOutboxRepository outboxRepository,
    IBalanceCacheService balanceCache,
    IDbConnectionFactory connectionFactory)
{
    public async Task<TransactionResponse> ExecuteAsync(
        CreateTransactionRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken ct)
    {
        // 1. Open connection + begin transaction
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        using var tx = await connection.BeginTransactionAsync(ct);

        // 2. Lock both accounts with SELECT ... FOR UPDATE
        var debitAccount = await accountRepository.GetForUpdateAsync(
            request.DebitAccountId, connection, tx, ct);
        var creditAccount = await accountRepository.GetForUpdateAsync(
            request.CreditAccountId, connection, tx, ct);

        // 3. Domain logic — debit and credit
        debitAccount.Debit(request.Amount);
        creditAccount.Credit(request.Amount);

        // 4. Persist transaction record
        var transaction = new Transaction { ... };
        await transactionRepository.InsertAsync(transaction, connection, tx, ct);

        // 5. Update account balances
        await accountRepository.UpdateBalanceAsync(debitAccount, connection, tx, ct);
        await accountRepository.UpdateBalanceAsync(creditAccount, connection, tx, ct);

        // 6. Persist outbox event in SAME transaction
        var outboxEvent = new OutboxEvent { ... };
        await outboxRepository.InsertAsync(outboxEvent, connection, tx, ct);

        // 7. Commit ACID transaction
        await tx.CommitAsync(ct);

        // 8. Invalidate balance cache synchronously (post-commit, same request)
        await balanceCache.InvalidateAsync(request.DebitAccountId, ct);
        await balanceCache.InvalidateAsync(request.CreditAccountId, ct);

        return new TransactionResponse { ... };
    }
}
```

#### GetBalanceUseCase (cache-aside pattern)

```csharp
public sealed class GetBalanceUseCase(
    IBalanceCacheService balanceCache,
    IAccountRepository accountRepository)
{
    public async Task<BalanceResponse> ExecuteAsync(Guid accountId, CancellationToken ct)
    {
        // 1. Try cache first
        var cached = await balanceCache.GetAsync(accountId, ct);
        if (cached.HasValue)
            return new BalanceResponse(accountId, cached.Value);

        // 2. Cache miss — query DB
        var account = await accountRepository.GetByIdAsync(accountId, ct);
        if (account is null)
            throw new AccountNotFoundException(accountId);

        // 3. Populate cache (TTL 5-10s)
        await balanceCache.SetAsync(accountId, account.Balance, ct);

        return new BalanceResponse(accountId, account.Balance);
    }
}
```

### 3. Infrastructure Layer

#### IDbConnectionFactory

```csharp
public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}

public sealed class NpgsqlConnectionFactory(IConfiguration config) : IDbConnectionFactory
{
    private readonly string _connectionString =
        config.GetConnectionString("LedgerDb")
        ?? throw new InvalidOperationException("LedgerDb connection string not configured.");

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}
```

#### AccountRepository (Dapper)

```csharp
public sealed class AccountRepository : IAccountRepository
{
    public async Task<Account?> GetForUpdateAsync(
        Guid id, NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        const string sql = @"
            SELECT id AS Id, holder_name AS HolderName, account_type AS AccountType,
                   balance AS Balance, created_at AS CreatedAt
            FROM accounts
            WHERE id = @Id
            FOR UPDATE";

        return await conn.QueryFirstOrDefaultAsync<Account>(
            new CommandDefinition(sql, new { Id = id }, tx, cancellationToken: ct));
    }

    public async Task UpdateBalanceAsync(
        Account account, NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        const string sql = "UPDATE accounts SET balance = @Balance WHERE id = @Id";
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { account.Balance, account.Id }, tx, cancellationToken: ct));
    }
}
```

#### RedisIdempotencyStore

```csharp
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync($"idempotency:{key}");
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetAsync(string key, string responsePayload, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync($"idempotency:{key}", responsePayload, Ttl);
    }
}
```

#### RedisBalanceCacheService

```csharp
public sealed class RedisBalanceCacheService(
    IConnectionMultiplexer redis,
    ILogger<RedisBalanceCacheService> logger) : IBalanceCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(7);

    public async Task<decimal?> GetAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync($"balance:{accountId}");
            return value.HasValue ? decimal.Parse(value!) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis cache read failed for account {AccountId}, falling back to DB", accountId);
            return null;
        }
    }

    public async Task SetAsync(Guid accountId, decimal balance, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync($"balance:{accountId}", balance.ToString(), Ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis cache write failed for account {AccountId}", accountId);
        }
    }

    public async Task InvalidateAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.KeyDeleteAsync($"balance:{accountId}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis cache invalidation failed for account {AccountId}", accountId);
        }
    }
}
```

### 4. API Layer — Minimal API Endpoints

#### AccountEndpoints

```csharp
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts", async (
            CreateAccountRequest request,
            CreateAccountUseCase useCase,
            CancellationToken ct) =>
        {
            var response = await useCase.ExecuteAsync(request, ct);
            return Results.Created($"/accounts/{response.Id}", response);
        })
        .WithName("CreateAccount")
        .Produces<AccountResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
```

#### TransactionEndpoints

```csharp
public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transactions", async (
            HttpContext httpContext,
            CreateTransactionRequest request,
            RecordTransactionUseCase useCase,
            IIdempotencyStore idempotencyStore,
            CancellationToken ct) =>
        {
            // Extract Idempotency-Key header
            if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey)
                || string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.Problem(
                    title: "Missing Idempotency-Key",
                    detail: "The Idempotency-Key header is required for transaction operations.",
                    statusCode: 400);
            }

            // Check idempotency store
            var existingResponse = await idempotencyStore.GetAsync(idempotencyKey!, ct);
            if (existingResponse is not null)
            {
                return Results.Ok(JsonSerializer.Deserialize<TransactionResponse>(existingResponse));
            }

            var correlationId = httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(request, idempotencyKey!, correlationId, ct);

            // Store in idempotency store
            await idempotencyStore.SetAsync(idempotencyKey!, JsonSerializer.Serialize(response), ct);

            return Results.Created($"/transactions/{response.Id}", response);
        })
        .WithName("RecordTransaction")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .Produces<TransactionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/transactions/{id:guid}", async (
            Guid id,
            GetTransactionUseCase useCase,
            CancellationToken ct) =>
        {
            var response = await useCase.ExecuteAsync(id, ct);
            return response is not null ? Results.Ok(response) : Results.Problem(
                title: "Transaction Not Found",
                statusCode: 404);
        })
        .WithName("GetTransaction")
        .Produces<TransactionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
```

#### HealthEndpoints

```csharp
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .WithName("LivenessProbe")
            .ExcludeFromDescription();

        app.MapGet("/health/ready", async (
            IDbConnectionFactory connectionFactory,
            IConnectionMultiplexer redis,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var checks = new Dictionary<string, string>();

            // PostgreSQL check
            try
            {
                using var conn = connectionFactory.CreateConnection();
                await conn.OpenAsync(ct);
                await conn.ExecuteScalarAsync(new CommandDefinition("SELECT 1", cancellationToken: ct));
                checks["postgresql"] = "Healthy";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PostgreSQL readiness check failed");
                checks["postgresql"] = "Unhealthy";
            }

            // Redis check
            try
            {
                var db = redis.GetDatabase();
                await db.PingAsync();
                checks["redis"] = "Healthy";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Redis readiness check failed");
                checks["redis"] = "Unhealthy";
            }

            var allHealthy = checks.Values.All(v => v == "Healthy");
            var result = new { status = allHealthy ? "Healthy" : "Unhealthy", checks };

            return allHealthy ? Results.Ok(result) : Results.Json(result, statusCode: 503);
        })
        .WithName("ReadinessProbe")
        .ExcludeFromDescription();
    }
}
```

### 5. Program.cs (Composition Root)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.WithProperty("ServiceName", "LedgerCore")
    .WriteTo.Console(new JsonFormatter()));

// Database
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

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

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

// Swagger
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
```

### 6. Middleware

#### GlobalExceptionMiddleware

Catches unhandled exceptions and returns RFC 7807 Problem Details. Maps domain exceptions (InsufficientBalanceException → 422, AccountNotFoundException → 404) to appropriate HTTP status codes. Logs full exception internally via Serilog.

#### CorrelationIdMiddleware

Extracts or generates X-Correlation-ID header, stores in AsyncLocal for logging enrichment, and propagates to outgoing responses.

### 7. Database Schema

```sql
CREATE TABLE accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    holder_name VARCHAR(200) NOT NULL,
    account_type VARCHAR(10) NOT NULL CHECK (account_type IN ('DEBIT', 'CREDIT')),
    balance DECIMAL(18, 4) NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE TABLE transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    idempotency_key VARCHAR(100) NOT NULL UNIQUE,
    debit_account_id UUID NOT NULL REFERENCES accounts(id),
    credit_account_id UUID NOT NULL REFERENCES accounts(id),
    amount DECIMAL(18, 4) NOT NULL CHECK (amount > 0),
    description VARCHAR(500),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_transactions_debit_account ON transactions(debit_account_id, created_at DESC);
CREATE INDEX idx_transactions_credit_account ON transactions(credit_account_id, created_at DESC);

CREATE TABLE outbox_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    published_at TIMESTAMP WITH TIME ZONE,
    retry_count INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_outbox_unpublished ON outbox_events(created_at) WHERE published_at IS NULL;

CREATE TABLE idempotency_keys (
    key VARCHAR(100) PRIMARY KEY,
    response_payload JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE INDEX idx_idempotency_expires ON idempotency_keys(expires_at);
```

### 8. Resilience Configuration (Polly)

```csharp
// Database retry pipeline
builder.Services.AddResiliencePipeline("db-retry", pipeline =>
{
    pipeline.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.ExponentialWithJitter,
        ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(ex => ex.IsTransient)
    });
});
```

## Data Flow — Record Transaction

```
Product Service → POST /transactions (Idempotency-Key, X-Correlation-ID)
    │
    ▼
CorrelationIdMiddleware → extract/generate correlation ID
    │
    ▼
TransactionEndpoint → validate Idempotency-Key header present
    │
    ▼
IdempotencyStore.GetAsync() → Redis check
    │
    ├── HIT → return cached response (HTTP 200)
    │
    └── MISS → continue
         │
         ▼
    RecordTransactionUseCase.ExecuteAsync()
         │
         ▼
    BEGIN TRANSACTION
         │
         ▼
    SELECT ... FOR UPDATE (debit account)
    SELECT ... FOR UPDATE (credit account)
         │
         ▼
    Domain: debit.Debit(amount), credit.Credit(amount)
         │
         ▼
    INSERT INTO transactions (...)
    UPDATE accounts SET balance (debit)
    UPDATE accounts SET balance (credit)
    INSERT INTO outbox_events (...)
         │
         ▼
    COMMIT
         │
         ▼
    BalanceCache.InvalidateAsync(debitAccountId)
    BalanceCache.InvalidateAsync(creditAccountId)
         │
         ▼
    IdempotencyStore.SetAsync(key, response)
         │
         ▼
    Return TransactionResponse (HTTP 201)
```

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| API style | Minimal APIs | Simpler, less ceremony, recommended for internal services |
| ORM | Dapper | Full SQL control, performance, matches org standards |
| Architecture | Clean Architecture (4 layers) | Testability, separation of concerns |
| Money type | C# decimal + DECIMAL(18,4) | Precision guarantee, no floating point errors |
| Serialization | SELECT ... FOR UPDATE | Prevents race conditions on same account |
| Cache invalidation | Synchronous post-commit | Strong consistency for balance reads |
| Idempotency | Redis primary + DB fallback | Fast check with durability guarantee |
| Health checks | Custom endpoints (not Microsoft.Extensions.Diagnostics.HealthChecks) | Simpler for internal service with only 2 dependencies |
| Outbox | Insert in same ACID tx | Guaranteed event delivery without dual-write |
