# Design Document

## Overview

The Product Service follows Clean Architecture with four layers: Domain, Application, Infrastructure, and API. It is a C# .NET 8 Minimal API application using Dapper for its own PostgreSQL database, CorePoints.Resilience (ILedgerClient) for Ledger communication, CorePoints.Caching for Redis, CorePoints.FeatureToggles for feature gating, and Serilog for structured logging. The application runs on ECS Fargate at port 8080, exposed via API Gateway with JWT authorization.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│              API Layer (Minimal API Endpoints)           │
│  Cashback, Transfer, Balance, Statement, Transaction,   │
│  Health — JWT claims extraction, Idempotency filter     │
├─────────────────────────────────────────────────────────┤
│               Application Layer (Use Cases)             │
│  CreditCashback, ExecuteTransfer, GetBalance,           │
│  GetStatement, GetTransaction                           │
├─────────────────────────────────────────────────────────┤
│                    Domain Layer                          │
│  Entities: CashbackRule, TransferLimit                  │
│  Services: CashbackCalculator, TransferValidator        │
├─────────────────────────────────────────────────────────┤
│               Infrastructure Layer                      │
│  Repositories (Dapper), ILedgerClient, Redis,           │
│  IdempotencyStore, OutboxRepository                     │
└─────────────────────────────────────────────────────────┘
         │                              │
         ▼                              ▼
   Product PostgreSQL              Ledger Core (HTTP)
   (rules, outbox, keys)          (via Cloud Map)
```

## Project Structure

```
src/CorePoints.ProductService/
├── Program.cs
├── Domain/
│   ├── Entities/
│   │   ├── CashbackRule.cs
│   │   └── TransferLimit.cs
│   ├── Services/
│   │   ├── CashbackCalculator.cs
│   │   └── TransferValidator.cs
│   └── Exceptions/
│       ├── IneligibleCashbackException.cs
│       ├── TransferLimitExceededException.cs
│       └── AccountAccessDeniedException.cs
├── Application/
│   ├── UseCases/
│   │   ├── CreditCashback/
│   │   │   ├── CreditCashbackUseCase.cs
│   │   │   ├── CreditCashbackRequest.cs
│   │   │   └── CashbackResponse.cs
│   │   ├── ExecuteTransfer/
│   │   │   ├── ExecuteTransferUseCase.cs
│   │   │   ├── TransferRequest.cs
│   │   │   └── TransferResponse.cs
│   │   ├── GetBalance/
│   │   │   ├── GetBalanceUseCase.cs
│   │   │   └── BalanceResponse.cs
│   │   ├── GetStatement/
│   │   │   ├── GetStatementUseCase.cs
│   │   │   └── StatementResponse.cs
│   │   └── GetTransaction/
│   │       ├── GetTransactionUseCase.cs
│   │       └── TransactionDetailResponse.cs
│   └── Interfaces/
│       ├── ICashbackRuleRepository.cs
│       ├── ITransferLimitRepository.cs
│       ├── ITransferHistoryRepository.cs
│       ├── IOutboxRepository.cs
│       └── IIdempotencyStore.cs
├── Infrastructure/
│   ├── Data/
│   │   ├── DbConnectionFactory.cs
│   │   ├── CashbackRuleRepository.cs
│   │   ├── TransferLimitRepository.cs
│   │   ├── TransferHistoryRepository.cs
│   │   └── OutboxRepository.cs
│   ├── Idempotency/
│   │   └── RedisIdempotencyStore.cs
│   └── Ledger/
│       └── LedgerResponseMapper.cs
├── Api/
│   ├── Endpoints/
│   │   ├── CashbackEndpoints.cs
│   │   ├── TransferEndpoints.cs
│   │   ├── AccountEndpoints.cs
│   │   ├── TransactionEndpoints.cs
│   │   └── HealthEndpoints.cs
│   ├── Middleware/
│   │   └── GlobalExceptionMiddleware.cs
│   └── Filters/
│       ├── IdempotencyFilter.cs
│       └── FeatureToggleFilter.cs
└── Configuration/
    └── ServiceRegistration.cs
```

## Component Design

### 1. Domain Layer

#### CashbackRule Entity

```csharp
public sealed record CashbackRule(
    Guid Id,
    string Name,
    decimal Percentage,
    decimal MinAmount,
    decimal MaxAmount,
    bool IsActive,
    string[] TargetGroups);
```

#### TransferLimit Entity

```csharp
public sealed record TransferLimit(
    Guid Id,
    string AccountType,
    decimal DailyLimit,
    decimal PerTransactionLimit);
```

#### CashbackCalculator (Domain Service)

```csharp
public static class CashbackCalculator
{
    public static decimal Calculate(decimal transactionAmount, CashbackRule rule)
    {
        if (transactionAmount < rule.MinAmount || transactionAmount > rule.MaxAmount)
            throw new IneligibleCashbackException("Transaction amount outside rule bounds.");

        return transactionAmount * (rule.Percentage / 100m);
    }

    public static bool IsEligible(string accountGroup, CashbackRule rule)
        => rule.IsActive && rule.TargetGroups.Contains(accountGroup);
}
```

#### TransferValidator (Domain Service)

```csharp
public static class TransferValidator
{
    public static void Validate(
        decimal amount,
        decimal dailyTotalSoFar,
        TransferLimit limit)
    {
        if (amount > limit.PerTransactionLimit)
            throw new TransferLimitExceededException(
                $"Amount {amount} exceeds per-transaction limit {limit.PerTransactionLimit}.");

        if (dailyTotalSoFar + amount > limit.DailyLimit)
            throw new TransferLimitExceededException(
                $"Transfer would exceed daily limit of {limit.DailyLimit}.");
    }
}
```

### 2. Application Layer — Use Cases

#### CreditCashbackUseCase

```csharp
public sealed class CreditCashbackUseCase(
    ICashbackRuleRepository cashbackRuleRepo,
    ILedgerClient ledgerClient,
    IOutboxRepository outboxRepo,
    IDbConnectionFactory connectionFactory,
    ICorrelationIdAccessor correlationId)
{
    public async Task<CashbackResponse> ExecuteAsync(
        CreditCashbackRequest request,
        string correlationIdValue,
        CancellationToken ct)
    {
        // 1. Load active cashback rule for the account group
        var rule = await cashbackRuleRepo.GetActiveRuleAsync(request.AccountGroup, ct)
            ?? throw new IneligibleCashbackException("No active cashback rule.");

        // 2. Validate eligibility
        if (!CashbackCalculator.IsEligible(request.AccountGroup, rule))
            throw new IneligibleCashbackException("Account group not eligible.");

        // 3. Calculate cashback (decimal only)
        var cashbackAmount = CashbackCalculator.Calculate(request.TransactionAmount, rule);

        // 4. Generate idempotency key for Ledger call
        var ledgerIdempotencyKey = Guid.NewGuid().ToString();

        // 5. Call Ledger to credit cashback
        var ledgerResponse = await ledgerClient.PostTransactionAsync(
            new { DebitAccountId = request.SystemSourceAccountId,
                  CreditAccountId = request.UserAccountId,
                  Amount = cashbackAmount,
                  Description = $"Cashback: {rule.Name}" },
            ledgerIdempotencyKey,
            correlationIdValue,
            ct);

        ledgerResponse.EnsureSuccessStatusCode();
        var txResult = await ledgerResponse.Content
            .ReadFromJsonAsync<LedgerTransactionResult>(ct);

        // 6. Persist outbox event in Product DB
        using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await outboxRepo.InsertAsync(new OutboxEvent
        {
            EventType = "CashbackCredited",
            Payload = JsonSerializer.Serialize(new {
                txResult!.TransactionId,
                request.UserAccountId,
                CashbackAmount = cashbackAmount,
                request.OriginalTransactionRef
            }),
            CorrelationId = correlationIdValue
        }, conn, ct);

        return new CashbackResponse(txResult.TransactionId, cashbackAmount);
    }
}
```

#### ExecuteTransferUseCase

```csharp
public sealed class ExecuteTransferUseCase(
    ITransferLimitRepository limitRepo,
    ITransferHistoryRepository historyRepo,
    ILedgerClient ledgerClient,
    IOutboxRepository outboxRepo,
    IDbConnectionFactory connectionFactory)
{
    public async Task<TransferResponse> ExecuteAsync(
        TransferRequest request,
        string correlationId,
        CancellationToken ct)
    {
        // 1. Load transfer limits for source account type
        var limit = await limitRepo.GetByAccountTypeAsync(request.SourceAccountType, ct)
            ?? throw new TransferLimitExceededException("No limit config found.");

        // 2. Get today's transfer total for source account
        var dailyTotal = await historyRepo
            .GetDailyTotalAsync(request.SourceAccountId, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        // 3. Validate limits (domain service)
        TransferValidator.Validate(request.Amount, dailyTotal, limit);

        // 4. Generate idempotency key for Ledger
        var ledgerIdempotencyKey = Guid.NewGuid().ToString();

        // 5. Call Ledger
        var ledgerResponse = await ledgerClient.PostTransactionAsync(
            new { DebitAccountId = request.SourceAccountId,
                  CreditAccountId = request.DestinationAccountId,
                  Amount = request.Amount,
                  Description = $"Transfer: {request.SourceAccountId} → {request.DestinationAccountId}" },
            ledgerIdempotencyKey,
            correlationId,
            ct);

        // 6. Handle Ledger errors (422 = insufficient balance)
        if (!ledgerResponse.IsSuccessStatusCode)
            return LedgerResponseMapper.MapError<TransferResponse>(ledgerResponse);

        var txResult = await ledgerResponse.Content
            .ReadFromJsonAsync<LedgerTransactionResult>(ct);

        // 7. Persist outbox event
        using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await outboxRepo.InsertAsync(new OutboxEvent
        {
            EventType = "TransferCompleted",
            Payload = JsonSerializer.Serialize(new {
                txResult!.TransactionId,
                request.SourceAccountId,
                request.DestinationAccountId,
                request.Amount
            }),
            CorrelationId = correlationId
        }, conn, ct);

        return new TransferResponse(txResult.TransactionId, request.Amount);
    }
}
```

#### GetBalanceUseCase

```csharp
public sealed class GetBalanceUseCase(
    ILedgerClient ledgerClient,
    ProductDataCacheService cache)
{
    public async Task<BalanceResponse> ExecuteAsync(
        Guid accountId, string correlationId, CancellationToken ct)
    {
        // 1. Check cache (5s TTL via CorePoints.Caching)
        var cacheKey = $"product:balance:{accountId}";
        var cached = await cache.GetAsync<BalanceResponse>(cacheKey, ct);
        if (cached is not null) return cached;

        // 2. Proxy to Ledger
        var response = await ledgerClient.GetBalanceAsync(
            accountId.ToString(), correlationId, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new AccountNotFoundException(accountId);

        response.EnsureSuccessStatusCode();
        var ledgerBalance = await response.Content
            .ReadFromJsonAsync<LedgerBalanceResult>(ct);

        // 3. Map to Product DTO and cache
        var result = new BalanceResponse(accountId, ledgerBalance!.Balance);
        await cache.SetAsync(cacheKey, result, TimeSpan.FromSeconds(5), ct);

        return result;
    }
}
```

#### GetStatementUseCase

```csharp
public sealed class GetStatementUseCase(ILedgerClient ledgerClient)
{
    public async Task<StatementResponse> ExecuteAsync(
        Guid accountId, int page, int pageSize,
        string correlationId, CancellationToken ct)
    {
        var response = await ledgerClient.GetStatementAsync(
            accountId.ToString(), correlationId, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new AccountNotFoundException(accountId);

        response.EnsureSuccessStatusCode();
        var ledgerStatement = await response.Content
            .ReadFromJsonAsync<LedgerStatementResult>(ct);

        return LedgerResponseMapper.ToStatementResponse(ledgerStatement!);
    }
}
```

### 3. Infrastructure Layer

#### DbConnectionFactory

```csharp
public sealed class NpgsqlConnectionFactory(IConfiguration config) : IDbConnectionFactory
{
    private readonly string _connectionString =
        config.GetConnectionString("ProductDb")
        ?? throw new InvalidOperationException("ProductDb connection string not configured.");

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}
```

#### CashbackRuleRepository (Dapper)

```csharp
public sealed class CashbackRuleRepository(IDbConnectionFactory connFactory)
    : ICashbackRuleRepository
{
    public async Task<CashbackRule?> GetActiveRuleAsync(string accountGroup, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, name, percentage, min_amount AS MinAmount, max_amount AS MaxAmount,
                   is_active AS IsActive, target_groups AS TargetGroups
            FROM cashback_rules
            WHERE is_active = true AND @AccountGroup = ANY(target_groups)
            ORDER BY percentage DESC
            LIMIT 1";

        using var conn = connFactory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<CashbackRule>(
            new CommandDefinition(sql, new { AccountGroup = accountGroup }, cancellationToken: ct));
    }
}
```

#### RedisIdempotencyStore

```csharp
public sealed class RedisIdempotencyStore(
    IConnectionMultiplexer redis,
    IDbConnectionFactory connFactory,
    ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync($"product:idempotency:{key}");
            if (value.HasValue) return value.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis idempotency read failed, falling back to DB");
        }

        // DB fallback
        using var conn = connFactory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT response_payload FROM idempotency_keys WHERE key = @Key AND expires_at > NOW()",
                new { Key = key }, cancellationToken: ct));
    }

    public async Task SetAsync(string key, string responsePayload, CancellationToken ct)
    {
        // Write to both Redis and DB
        try
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync($"product:idempotency:{key}", responsePayload, Ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis idempotency write failed for key {Key}", key);
        }

        using var conn = connFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO idempotency_keys (key, response_payload, expires_at)
              VALUES (@Key, @Payload::jsonb, @ExpiresAt)
              ON CONFLICT (key) DO NOTHING",
            new { Key = key, Payload = responsePayload, ExpiresAt = DateTime.UtcNow.Add(Ttl) },
            cancellationToken: ct));
    }
}
```

#### LedgerResponseMapper

```csharp
public static class LedgerResponseMapper
{
    public static async Task<T> MapError<T>(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;

        if (statusCode == 422)
            throw new InsufficientBalanceException();
        if (statusCode == 404)
            throw new AccountNotFoundException();

        throw new LedgerUnavailableException(
            $"Ledger returned unexpected status: {statusCode}");
    }

    public static StatementResponse ToStatementResponse(LedgerStatementResult ledger)
        => new(ledger.Items.Select(i => new StatementItem(
            i.Id, i.Amount, i.Description, i.CreatedAt)).ToList(),
            ledger.Page, ledger.PageSize, ledger.TotalCount);
}
```

### 4. API Layer — Endpoints

#### CashbackEndpoints

```csharp
public static class CashbackEndpoints
{
    public static void MapCashbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/cashback/credit", async (
            HttpContext ctx,
            CreditCashbackRequest request,
            CreditCashbackUseCase useCase,
            IIdempotencyStore idempotencyStore,
            IFeatureToggleService featureToggles,
            CancellationToken ct) =>
        {
            // Feature gate
            if (!await featureToggles.IsEnabledAsync("cashback", ct))
                return Results.Problem(title: "Feature Unavailable",
                    detail: "Cashback is currently disabled.", statusCode: 503);

            // Idempotency-Key validation
            if (!ctx.Request.Headers.TryGetValue("Idempotency-Key", out var key)
                || string.IsNullOrWhiteSpace(key))
                return Results.Problem(title: "Missing Idempotency-Key", statusCode: 400);

            // Check idempotency
            var existing = await idempotencyStore.GetAsync(key!, ct);
            if (existing is not null)
                return Results.Ok(JsonSerializer.Deserialize<CashbackResponse>(existing));

            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(request, correlationId, ct);

            await idempotencyStore.SetAsync(key!,
                JsonSerializer.Serialize(response), ct);

            return Results.Created($"/api/v1/transactions/{response.TransactionId}", response);
        })
        .RequireAuthorization()
        .WithName("CreditCashback")
        .Produces<CashbackResponse>(201)
        .ProducesProblem(400).ProducesProblem(422).ProducesProblem(503);
    }
}
```

#### TransferEndpoints

```csharp
public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/transfers", async (
            HttpContext ctx,
            TransferRequest request,
            ExecuteTransferUseCase useCase,
            IIdempotencyStore idempotencyStore,
            IFeatureToggleService featureToggles,
            CancellationToken ct) =>
        {
            if (!await featureToggles.IsEnabledAsync("transfers", ct))
                return Results.Problem(title: "Feature Unavailable",
                    detail: "Transfers are currently disabled.", statusCode: 503);

            if (!ctx.Request.Headers.TryGetValue("Idempotency-Key", out var key)
                || string.IsNullOrWhiteSpace(key))
                return Results.Problem(title: "Missing Idempotency-Key", statusCode: 400);

            var existing = await idempotencyStore.GetAsync(key!, ct);
            if (existing is not null)
                return Results.Ok(JsonSerializer.Deserialize<TransferResponse>(existing));

            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(request, correlationId, ct);

            await idempotencyStore.SetAsync(key!,
                JsonSerializer.Serialize(response), ct);

            return Results.Created($"/api/v1/transactions/{response.TransactionId}", response);
        })
        .RequireAuthorization()
        .WithName("ExecuteTransfer")
        .Produces<TransferResponse>(201)
        .ProducesProblem(400).ProducesProblem(422).ProducesProblem(503);
    }
}
```

#### AccountEndpoints

```csharp
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/accounts/{id:guid}/balance", async (
            Guid id, HttpContext ctx,
            GetBalanceUseCase useCase,
            CancellationToken ct) =>
        {
            // Authorization: verify account ownership
            var userId = ctx.User.FindFirst("sub")?.Value;
            // Account ownership check delegated to use case or a service

            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(id, correlationId, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization()
        .WithName("GetBalance")
        .Produces<BalanceResponse>(200)
        .ProducesProblem(403).ProducesProblem(404);

        app.MapGet("/api/v1/accounts/{id:guid}/statement", async (
            Guid id, int? page, int? pageSize, HttpContext ctx,
            GetStatementUseCase useCase,
            CancellationToken ct) =>
        {
            var p = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 20, 1, 100);

            var correlationId = ctx.Request.Headers["X-Correlation-ID"]
                .FirstOrDefault() ?? Guid.NewGuid().ToString();

            var response = await useCase.ExecuteAsync(id, p, ps, correlationId, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization()
        .WithName("GetStatement")
        .Produces<StatementResponse>(200)
        .ProducesProblem(403).ProducesProblem(404);
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
            .AllowAnonymous();

        app.MapGet("/health/ready", async (
            IDbConnectionFactory connFactory,
            IConnectionMultiplexer redis,
            ILedgerClient ledgerClient,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var checks = new Dictionary<string, string>();

            try
            {
                using var conn = connFactory.CreateConnection();
                await conn.OpenAsync(ct);
                await conn.ExecuteScalarAsync(new CommandDefinition("SELECT 1", cancellationToken: ct));
                checks["postgresql"] = "Healthy";
            }
            catch { checks["postgresql"] = "Unhealthy"; }

            try
            {
                await redis.GetDatabase().PingAsync();
                checks["redis"] = "Healthy";
            }
            catch { checks["redis"] = "Unhealthy"; }

            try
            {
                var resp = await ledgerClient.GetBalanceAsync("health-probe", "", ct);
                checks["ledger"] = resp.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable
                    ? "Healthy" : "Unhealthy";
            }
            catch { checks["ledger"] = "Unhealthy"; }

            var allHealthy = checks.Values.All(v => v == "Healthy");
            return allHealthy
                ? Results.Ok(new { status = "Healthy", checks })
                : Results.Json(new { status = "Unhealthy", checks }, statusCode: 503);
        }).AllowAnonymous();
    }
}
```

### 5. Middleware and Filters

#### IdempotencyFilter (reusable endpoint filter)

Extracts Idempotency-Key, checks store, short-circuits with cached response or continues pipeline. Applied to write endpoints via `.AddEndpointFilter<IdempotencyFilter>()`.

#### FeatureToggleFilter

Checks feature toggle state before executing endpoint logic. Returns 503 when disabled.

#### GlobalExceptionMiddleware

Maps domain exceptions to HTTP responses:
- `IneligibleCashbackException` → 422
- `TransferLimitExceededException` → 422
- `AccountNotFoundException` → 404
- `AccountAccessDeniedException` → 403
- `InsufficientBalanceException` → 422
- `LedgerUnavailableException` → 503
- `BrokenCircuitException` (Polly) → 503

All errors use RFC 7807 Problem Details. Stack traces never exposed.

### 6. Database Schema (Product Service)

```sql
CREATE TABLE cashback_rules (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    percentage DECIMAL(5, 2) NOT NULL CHECK (percentage > 0 AND percentage <= 100),
    min_amount DECIMAL(18, 4) NOT NULL CHECK (min_amount >= 0),
    max_amount DECIMAL(18, 4) NOT NULL CHECK (max_amount > min_amount),
    is_active BOOLEAN NOT NULL DEFAULT true,
    target_groups TEXT[] NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE TABLE transfer_limits (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    account_type VARCHAR(50) NOT NULL UNIQUE,
    daily_limit DECIMAL(18, 4) NOT NULL CHECK (daily_limit > 0),
    per_transaction_limit DECIMAL(18, 4) NOT NULL CHECK (per_transaction_limit > 0),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE TABLE transfer_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_account_id UUID NOT NULL,
    destination_account_id UUID NOT NULL,
    amount DECIMAL(18, 4) NOT NULL CHECK (amount > 0),
    ledger_transaction_id UUID NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_transfer_history_source_date
    ON transfer_history(source_account_id, created_at DESC);

CREATE TABLE outbox_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    correlation_id VARCHAR(100),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    published_at TIMESTAMP WITH TIME ZONE,
    retry_count INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_outbox_unpublished
    ON outbox_events(created_at) WHERE published_at IS NULL;

CREATE TABLE idempotency_keys (
    key VARCHAR(100) PRIMARY KEY,
    response_payload JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE INDEX idx_idempotency_expires ON idempotency_keys(expires_at);
```

### 7. Program.cs (Composition Root)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.WithProperty("ServiceName", "ProductService")
    .WriteTo.Console(new JsonFormatter()));

// Database
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

// Redis (CorePoints.Caching)
builder.Services.AddCorePointsCaching(builder.Configuration);

// Feature Toggles
builder.Services.AddCorePointsFeatureToggles(builder.Configuration);

// Ledger Client (CorePoints.Resilience — includes Polly policies)
builder.Services.AddLedgerClient(builder.Configuration);

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

// Auth (JWT from Cognito — API Gateway validates, Product extracts claims)
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware
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
```

## Data Flow — Cashback Credit

```
Client (via API Gateway + JWT)
    │
    ▼
POST /api/v1/cashback/credit (Idempotency-Key, X-Correlation-ID)
    │
    ▼
GlobalExceptionMiddleware → FeatureToggle check (cashback)
    │
    ▼
IdempotencyStore.GetAsync(clientKey) → Redis → DB fallback
    │
    ├── HIT → return cached response (HTTP 200)
    │
    └── MISS → continue
         │
         ▼
    CreditCashbackUseCase.ExecuteAsync()
         │
         ▼
    Load CashbackRule from Product DB
    Validate eligibility + calculate amount (decimal)
         │
         ▼
    ILedgerClient.PostTransactionAsync(newIdempotencyKey, correlationId)
    → Ledger Core (Cloud Map HTTP, Polly retry + circuit breaker)
         │
         ▼
    Ledger returns TransactionResponse (HTTP 201)
         │
         ▼
    INSERT INTO outbox_events (CashbackCredited) — Product DB
         │
         ▼
    IdempotencyStore.SetAsync(clientKey, response)
         │
         ▼
    Return CashbackResponse (HTTP 201)
```

## Correctness Properties

### Property 1: Cashback Calculation Precision (Invariant)

For any transaction amount A and cashback rule with percentage P, the calculated cashback equals A × (P / 100) using decimal arithmetic with no floating-point loss.

**Validates:** Requirement 1.4, Requirement 1 acceptance criterion on decimal arithmetic.

### Property 2: Transfer Limit Enforcement (Metamorphic)

For any transfer amount X and daily total T, if X + T > daily_limit then the transfer is rejected. Corollary: reducing the amount to daily_limit - T always succeeds (limit-wise).

**Validates:** Requirement 2.2, 2.3.

### Property 3: Idempotency — Same Key Same Response (Idempotence)

For any write operation processed with Idempotency-Key K, calling the same endpoint with the same K produces an identical response payload without re-executing Ledger calls.

**Validates:** Requirement 6.2.

### Property 4: Ledger Key Isolation (Invariant)

For any client-provided Idempotency-Key K, the key sent to ILedgerClient is always a distinct newly generated UUID ≠ K.

**Validates:** Requirement 6.6.

### Property 5: Cashback Eligibility Guard (Error Condition)

For any transaction amount outside [min_amount, max_amount] of the active rule, the cashback endpoint returns 422 without calling the Ledger.

**Validates:** Requirement 1.2.

### Property 6: Daily Limit Accumulation (Invariant)

For any sequence of N approved transfers in a day, the sum of all transfer amounts never exceeds the configured daily_limit for that account type.

**Validates:** Requirement 2.3.

### Property 7: PageSize Bound (Invariant)

For any requested pageSize (clamped to [1, 100]), the statement response contains at most pageSize items.

**Validates:** Requirement 4.3.

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Ledger communication | ILedgerClient (CorePoints.Resilience) | Reuses shared library with built-in Polly policies |
| Caching | CorePoints.Caching (ProductDataCacheService) | Shared library, consistent cache-aside pattern |
| Feature gating | CorePoints.FeatureToggles | Shared library, runtime toggle without redeploy |
| Idempotency (Product) | Separate from Ledger key | Client key validates at Product; new key generated for each Ledger call |
| Outbox persistence | After Ledger confirmation, in Product DB | No dual-write; worker publishes later |
| Authorization | JWT claims in HttpContext.User | API Gateway validates token; Product checks ownership via sub claim |
| Domain validation | Pure static domain services | Testable without dependencies, no I/O |
| Error mapping | LedgerResponseMapper | Translates Ledger HTTP errors to Product domain exceptions |
| Transfer daily tracking | transfer_history table | Simple query for daily aggregation without depending on Ledger |
