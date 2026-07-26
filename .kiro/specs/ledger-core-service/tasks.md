# Implementation Tasks

## Task 1: Project Setup and Solution Structure

- [ ] 1.1 Create the .NET 8 project `CorePoints.LedgerCore` with Minimal API template and folder structure (Domain, Application, Infrastructure, Api, Configuration)
- [ ] 1.2 Add NuGet packages: Dapper, Npgsql, StackExchange.Redis, Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Formatting.Compact, Polly, Swashbuckle.AspNetCore, OpenTelemetry SDK packages
- [ ] 1.3 Create the test project `CorePoints.LedgerCore.Tests` with xUnit, Moq, FluentAssertions, and FsCheck (for property-based tests)
- [ ] 1.4 Configure `appsettings.json` and `appsettings.Development.json` with connection strings for PostgreSQL (via RDS Proxy), Redis, Serilog config, and OpenTelemetry OTLP endpoint

## Task 2: Domain Layer

- [ ] 2.1 Create `AccountType` enum (DEBIT, CREDIT) in Domain/ValueObjects/
- [ ] 2.2 Create `Account` entity with Id (Guid), HolderName (string), AccountType, Balance (decimal), CreatedAt. Include Debit(decimal) and Credit(decimal) methods with InsufficientBalanceException guard on Debit
- [ ] 2.3 Create `Transaction` entity with Id (Guid), IdempotencyKey (string), DebitAccountId (Guid), CreditAccountId (Guid), Amount (decimal), Description (string?), CreatedAt
- [ ] 2.4 Create `OutboxEvent` entity with Id (Guid), EventType (string), Payload (string/JSON), CreatedAt, PublishedAt (DateTime?), RetryCount (int)
- [ ] 2.5 Create domain exceptions: `InsufficientBalanceException`, `AccountNotFoundException`, `TransactionNotFoundException`
- [ ] 2.6 Write unit tests for Account.Debit() — validates balance reduction, throws InsufficientBalanceException when amount exceeds balance, uses decimal precision

## Task 3: Application Layer — Interfaces and DTOs

- [ ] 3.1 Create `IAccountRepository` interface with methods: GetByIdAsync, GetForUpdateAsync, InsertAsync, UpdateBalanceAsync, ExistsAsync
- [ ] 3.2 Create `ITransactionRepository` interface with methods: InsertAsync, GetByIdAsync, GetByAccountIdPaginatedAsync, CountByAccountIdAsync
- [ ] 3.3 Create `IOutboxRepository` interface with method: InsertAsync
- [ ] 3.4 Create `IIdempotencyStore` interface with methods: GetAsync(string key), SetAsync(string key, string responsePayload)
- [ ] 3.5 Create `IBalanceCacheService` interface with methods: GetAsync(Guid accountId), SetAsync(Guid accountId, decimal balance), InvalidateAsync(Guid accountId)
- [ ] 3.6 Create request/response DTOs: CreateAccountRequest, AccountResponse, CreateTransactionRequest, TransactionResponse, BalanceResponse, StatementRequest, PaginatedStatementResponse

## Task 4: Application Layer — Use Cases

- [ ] 4.1 Implement `CreateAccountUseCase` — validates request, generates UUID, inserts account with zero balance, returns AccountResponse
- [ ] 4.2 Implement `RecordTransactionUseCase` — opens transaction, locks accounts (FOR UPDATE), validates balance, debits/credits, inserts transaction + outbox event, commits, invalidates cache
- [ ] 4.3 Implement `GetBalanceUseCase` — cache-aside pattern: check Redis → fallback to DB → populate cache with 7s TTL
- [ ] 4.4 Implement `GetStatementUseCase` — validates account exists, queries paginated transactions (default page=1, pageSize=20, max 100), returns PaginatedStatementResponse with total count
- [ ] 4.5 Implement `GetTransactionUseCase` — queries by ID, throws TransactionNotFoundException if not found
- [ ] 4.6 Write unit tests for RecordTransactionUseCase — mock repositories, verify ACID flow: lock → debit → credit → insert → outbox → commit → invalidate cache
- [ ] 4.7 Write unit tests for GetBalanceUseCase — test cache hit, cache miss with DB fallback, account not found

## Task 5: Infrastructure Layer — Database

- [ ] 5.1 Implement `NpgsqlConnectionFactory` (IDbConnectionFactory) — reads connection string from config, returns new NpgsqlConnection. Register as Scoped in DI
- [ ] 5.2 Implement `AccountRepository` with Dapper — GetByIdAsync, GetForUpdateAsync (SELECT ... FOR UPDATE), InsertAsync (RETURNING id), UpdateBalanceAsync, ExistsAsync. All use CommandDefinition with CancellationToken
- [ ] 5.3 Implement `TransactionRepository` with Dapper — InsertAsync, GetByIdAsync, GetByAccountIdPaginatedAsync (LIMIT/OFFSET with ORDER BY created_at DESC), CountByAccountIdAsync
- [ ] 5.4 Implement `OutboxRepository` with Dapper — InsertAsync (accepts connection + transaction parameters for same-tx persistence)
- [ ] 5.5 Create SQL migration script with the full schema: accounts, transactions, outbox_events, idempotency_keys tables with indexes and constraints

## Task 6: Infrastructure Layer — Redis (Cache + Idempotency)

- [ ] 6.1 Implement `RedisBalanceCacheService` — GetAsync (try/catch with fallback to null on Redis failure), SetAsync (TTL 7 seconds), InvalidateAsync (KeyDelete). Log warnings on Redis failures, never throw
- [ ] 6.2 Implement `RedisIdempotencyStore` — GetAsync (StringGet with "idempotency:" prefix), SetAsync (StringSet with 24h TTL). On Redis failure, fallback to DB check via idempotency_keys table
- [ ] 6.3 Write unit tests for RedisBalanceCacheService — verify fallback to null on Redis exception, verify TTL is set correctly, verify invalidation deletes key
- [ ] 6.4 Write unit tests for RedisIdempotencyStore — verify key prefix, verify TTL, verify get returns null on miss

## Task 7: Infrastructure Layer — Resilience (Polly)

- [ ] 7.1 Configure Polly resilience pipeline "db-retry" — retry 3x with exponential backoff + jitter for transient NpgsqlException. Register in DI
- [ ] 7.2 Configure Polly resilience pipeline "redis-retry" — retry 2x with short delay for Redis transient failures

## Task 8: API Layer — Endpoints

- [ ] 8.1 Implement `AccountEndpoints` — MapPost("/accounts") with request validation, returns Created(201). MapGet("/accounts/{id}/balance") delegates to GetBalanceUseCase. MapGet("/accounts/{id}/statement") with page/pageSize query params
- [ ] 8.2 Implement `TransactionEndpoints` — MapPost("/transactions") with Idempotency-Key header extraction and validation, idempotency check, use case execution, idempotency store write. MapGet("/transactions/{id}") delegates to GetTransactionUseCase
- [ ] 8.3 Implement `HealthEndpoints` — MapGet("/health/live") returns 200 always. MapGet("/health/ready") checks PostgreSQL (SELECT 1) and Redis (PING), returns 503 with dependency status if any fails
- [ ] 8.4 Write integration-style tests for endpoint routing — verify correct HTTP methods, status codes, and response shapes using WebApplicationFactory

## Task 9: API Layer — Middleware and Error Handling

- [ ] 9.1 Implement `CorrelationIdMiddleware` — extract X-Correlation-ID from request headers (or generate new GUID), store in AsyncLocal/HttpContext.Items, add to response headers, enrich Serilog LogContext
- [ ] 9.2 Implement `GlobalExceptionMiddleware` — catch exceptions, map domain exceptions to HTTP status codes (AccountNotFoundException→404, InsufficientBalanceException→422, TransactionNotFoundException→404), return RFC 7807 Problem Details, log full exception via Serilog
- [ ] 9.3 Write unit tests for GlobalExceptionMiddleware — verify correct status codes for each domain exception type, verify Problem Details format, verify stack traces are never exposed

## Task 10: Program.cs Composition Root

- [ ] 10.1 Configure Serilog with JSON formatter, console sink, enrichment with ServiceName="LedgerCore", TraceId, SpanId
- [ ] 10.2 Register all DI services: IDbConnectionFactory, IConnectionMultiplexer (Redis singleton), repositories, cache/idempotency services, use cases
- [ ] 10.3 Configure OpenTelemetry: AddAspNetCoreInstrumentation, AddHttpClientInstrumentation, Npgsql instrumentation, OTLP exporter for traces and metrics
- [ ] 10.4 Configure Swagger/OpenAPI for internal documentation
- [ ] 10.5 Wire middleware pipeline (CorrelationId → GlobalException) and map all endpoint groups
- [ ] 10.6 Set Kestrel to listen on port 8080 for ECS Fargate compatibility

## Task 11: Dockerfile and Deployment Configuration

- [ ] 11.1 Create multi-stage Dockerfile: SDK image for build/publish, runtime image (aspnet:8.0-alpine), expose port 8080, set ASPNETCORE_URLS=http://+:8080
- [ ] 11.2 Create .dockerignore to exclude bin/, obj/, .git/, tests/
- [ ] 11.3 Create docker-compose.yml for local development with PostgreSQL 16 and Redis 7 containers, with volume mounts for the migration script

## Task 12: Property-Based Tests (Critical Invariants)

- [ ] 12.1 Write property test: For any valid transaction amount, the sum of debit account balance change and credit account balance change equals zero (conservation of money)
- [ ] 12.2 Write property test: For any sequence of transactions on an account, the final balance equals initial balance minus total debits plus total credits (balance invariant)
- [ ] 12.3 Write property test: Recording the same transaction twice with the same Idempotency-Key produces identical responses (idempotency property)
- [ ] 12.4 Write property test: For any valid pageSize (1-100) and page number, GetStatement returns at most pageSize items (pagination bound)
