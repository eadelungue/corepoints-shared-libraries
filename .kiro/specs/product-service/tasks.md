# Implementation Tasks

## Task 1: Project Setup and Solution Structure

- [ ] 1.1 Create the .NET 8 project `CorePoints.ProductService` with Minimal API template and folder structure (Domain, Application, Infrastructure, Api, Configuration)
- [ ] 1.2 Add NuGet packages: Dapper, Npgsql, Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Formatting.Compact, Swashbuckle.AspNetCore, OpenTelemetry SDK packages, Microsoft.AspNetCore.Authentication.JwtBearer
- [ ] 1.3 Add project references to CorePoints.Resilience, CorePoints.Caching, CorePoints.FeatureToggles
- [ ] 1.4 Create the test project `CorePoints.ProductService.Tests` with xUnit, Moq, FluentAssertions, FsCheck.Xunit
- [ ] 1.5 Configure `appsettings.json` and `appsettings.Development.json` with connection strings (ProductDb PostgreSQL, Redis), Ledger base URL (Cloud Map), Serilog config, OpenTelemetry OTLP endpoint, JWT settings

## Task 2: Domain Layer

- [ ] 2.1 Create `CashbackRule` sealed record in Domain/Entities/ with Id, Name, Percentage (decimal), MinAmount (decimal), MaxAmount (decimal), IsActive (bool), TargetGroups (string[])
- [ ] 2.2 Create `TransferLimit` sealed record in Domain/Entities/ with Id, AccountType (string), DailyLimit (decimal), PerTransactionLimit (decimal)
- [ ] 2.3 Create `CashbackCalculator` static class in Domain/Services/ — Calculate(amount, rule) returns decimal cashback, IsEligible(accountGroup, rule) returns bool. Uses decimal arithmetic exclusively
- [ ] 2.4 Create `TransferValidator` static class in Domain/Services/ — Validate(amount, dailyTotalSoFar, limit) throws TransferLimitExceededException when per-transaction or daily limit exceeded
- [ ] 2.5 Create domain exceptions: IneligibleCashbackException, TransferLimitExceededException, AccountAccessDeniedException, AccountNotFoundException, LedgerUnavailableException, InsufficientBalanceException
- [ ] 2.6 Write unit tests for CashbackCalculator — correct percentage calculation with decimal precision, throws for amount below min, throws for amount above max, eligibility check for target groups
- [ ] 2.7 Write unit tests for TransferValidator — accepts amount within limits, throws for per-transaction exceeded, throws for daily limit exceeded

## Task 3: Application Layer — Interfaces and DTOs

- [ ] 3.1 Create `ICashbackRuleRepository` interface with GetActiveRuleAsync(accountGroup, ct)
- [ ] 3.2 Create `ITransferLimitRepository` interface with GetByAccountTypeAsync(accountType, ct)
- [ ] 3.3 Create `ITransferHistoryRepository` interface with GetDailyTotalAsync(sourceAccountId, date, ct) and InsertAsync(transfer, conn, ct)
- [ ] 3.4 Create `IOutboxRepository` interface with InsertAsync(outboxEvent, connection, ct)
- [ ] 3.5 Create `IIdempotencyStore` interface with GetAsync(key, ct) and SetAsync(key, responsePayload, ct)
- [ ] 3.6 Create request DTOs: CreditCashbackRequest (UserAccountId, SystemSourceAccountId, TransactionAmount, AccountGroup, OriginalTransactionRef), TransferRequest (SourceAccountId, DestinationAccountId, Amount, SourceAccountType)
- [ ] 3.7 Create response DTOs: CashbackResponse (TransactionId, CashbackAmount), TransferResponse (TransactionId, Amount), BalanceResponse (AccountId, Balance), StatementResponse (Items, Page, PageSize, TotalCount), TransactionDetailResponse
- [ ] 3.8 Create internal Ledger mapping DTOs: LedgerTransactionResult, LedgerBalanceResult, LedgerStatementResult

## Task 4: Application Layer — Use Cases

- [ ] 4.1 Implement `CreditCashbackUseCase` — load active rule, validate eligibility, calculate cashback (decimal), generate Ledger idempotency key, call ILedgerClient.PostTransactionAsync, persist outbox event, return CashbackResponse
- [ ] 4.2 Implement `ExecuteTransferUseCase` — load transfer limit, get daily total from transfer_history, validate via TransferValidator, generate Ledger idempotency key, call ILedgerClient.PostTransactionAsync, handle Ledger 422 (insufficient balance), persist outbox event + transfer_history record, return TransferResponse
- [ ] 4.3 Implement `GetBalanceUseCase` — check cache (CorePoints.Caching, 5s TTL), call ILedgerClient.GetBalanceAsync on miss, map Ledger 404 to AccountNotFoundException, cache result, return BalanceResponse
- [ ] 4.4 Implement `GetStatementUseCase` — call ILedgerClient.GetStatementAsync with page/pageSize, map Ledger 404 to exception, convert to StatementResponse via LedgerResponseMapper
- [ ] 4.5 Implement `GetTransactionUseCase` — call ILedgerClient.GetTransactionAsync, map 404, return TransactionDetailResponse
- [ ] 4.6 Implement `LedgerResponseMapper` static class — maps Ledger HTTP errors to domain exceptions, converts Ledger DTOs to Product response DTOs
- [ ] 4.7 Write unit tests for CreditCashbackUseCase — mock repos and ILedgerClient, verify: rule loaded, eligibility checked, correct amount calculated, Ledger called with new idempotency key ≠ client key, outbox persisted after Ledger success
- [ ] 4.8 Write unit tests for ExecuteTransferUseCase — mock repos and ILedgerClient, verify: limit loaded, daily total checked, Ledger called, outbox persisted, 422 from Ledger maps to InsufficientBalanceException

## Task 5: Infrastructure Layer — Database

- [ ] 5.1 Implement `NpgsqlConnectionFactory` (IDbConnectionFactory) — reads ProductDb connection string, returns NpgsqlConnection. Register as Scoped
- [ ] 5.2 Implement `CashbackRuleRepository` with Dapper — GetActiveRuleAsync queries cashback_rules with is_active=true and target_groups array contains. Uses CommandDefinition with CancellationToken
- [ ] 5.3 Implement `TransferLimitRepository` with Dapper — GetByAccountTypeAsync queries transfer_limits by account_type
- [ ] 5.4 Implement `TransferHistoryRepository` with Dapper — GetDailyTotalAsync (SUM of amount for source_account_id where created_at is today), InsertAsync
- [ ] 5.5 Implement `OutboxRepository` with Dapper — InsertAsync (accepts NpgsqlConnection for transactional use with use cases)
- [ ] 5.6 Create SQL migration script with full schema: cashback_rules, transfer_limits, transfer_history, outbox_events, idempotency_keys tables with indexes, constraints, and seed data for default rules/limits

## Task 6: Infrastructure Layer — Idempotency Store

- [ ] 6.1 Implement `RedisIdempotencyStore` — GetAsync checks Redis (product:idempotency:{key}) first, falls back to DB idempotency_keys table on Redis failure. SetAsync writes to both Redis (24h TTL) and DB (ON CONFLICT DO NOTHING). Log warnings on Redis failures, never throw
- [ ] 6.2 Write unit tests for RedisIdempotencyStore — verify Redis-first check, DB fallback on Redis exception, dual-write behavior, TTL configuration

## Task 7: API Layer — Endpoints

- [ ] 7.1 Implement `CashbackEndpoints` — MapPost("/api/v1/cashback/credit") with Idempotency-Key extraction, feature toggle check, idempotency store check, use case execution, store response. RequireAuthorization()
- [ ] 7.2 Implement `TransferEndpoints` — MapPost("/api/v1/transfers") with same pattern as cashback (idempotency, feature toggle, use case, store). RequireAuthorization()
- [ ] 7.3 Implement `AccountEndpoints` — MapGet("/api/v1/accounts/{id}/balance") with JWT ownership check, delegates to GetBalanceUseCase. MapGet("/api/v1/accounts/{id}/statement") with page/pageSize clamping (default 20, max 100). RequireAuthorization()
- [ ] 7.4 Implement `TransactionEndpoints` — MapGet("/api/v1/transactions/{id}") delegates to GetTransactionUseCase. RequireAuthorization()
- [ ] 7.5 Implement `HealthEndpoints` — MapGet("/health/live") returns 200 always. MapGet("/health/ready") checks PostgreSQL + Redis + Ledger connectivity, returns 503 with dependency status if any fails. AllowAnonymous()

## Task 8: API Layer — Middleware and Filters

- [ ] 8.1 Implement `GlobalExceptionMiddleware` — maps domain exceptions to HTTP status codes (IneligibleCashback→422, TransferLimitExceeded→422, AccountNotFound→404, AccessDenied→403, InsufficientBalance→422, LedgerUnavailable→503, BrokenCircuitException→503), returns RFC 7807 Problem Details, logs full exception via Serilog, never exposes stack traces
- [ ] 8.2 Implement `IdempotencyFilter` (IEndpointFilter) — reusable filter that extracts Idempotency-Key, returns 400 if missing, checks store, short-circuits with cached response (200), or continues pipeline
- [ ] 8.3 Implement `FeatureToggleFilter` (IEndpointFilter) — accepts feature name parameter, checks CorePoints.FeatureToggles, returns 503 Problem Details if disabled
- [ ] 8.4 Write unit tests for GlobalExceptionMiddleware — verify each domain exception maps to correct HTTP status, verify Problem Details format, verify no stack traces in response

## Task 9: Authorization

- [ ] 9.1 Configure JWT Bearer authentication — reads Cognito issuer/audience from config, registers AddAuthentication().AddJwtBearer()
- [ ] 9.2 Implement account ownership verification — extract sub claim from HttpContext.User, compare with account ownership (via a lightweight lookup or convention). Return 403 for unauthorized access
- [ ] 9.3 Write unit tests for authorization — verify 403 when sub claim doesn't match account, verify health endpoints allow anonymous

## Task 10: Program.cs Composition Root

- [ ] 10.1 Configure Serilog with JSON formatter, console sink, enrichment with ServiceName="ProductService", TraceId, SpanId
- [ ] 10.2 Register all DI services: IDbConnectionFactory, repositories, idempotency store, use cases, CorePoints.Caching, CorePoints.FeatureToggles, ILedgerClient (via AddLedgerClient)
- [ ] 10.3 Configure OpenTelemetry: AddAspNetCoreInstrumentation, AddHttpClientInstrumentation, OTLP exporter for traces and metrics
- [ ] 10.4 Configure JWT authentication and authorization
- [ ] 10.5 Configure Swagger/OpenAPI
- [ ] 10.6 Wire middleware pipeline (GlobalException → Authentication → Authorization) and map all endpoint groups
- [ ] 10.7 Set Kestrel to listen on port 8080 for ECS Fargate compatibility

## Task 11: Observability

- [ ] 11.1 Integrate CorePoints.ApiGateway CorrelationIdMiddleware — extract/generate X-Correlation-ID, enrich Serilog LogContext, propagate to responses and ILedgerClient calls
- [ ] 11.2 Add structured log statements to use cases — log cashback calculations, transfer validations, Ledger call results with relevant IDs (no PII)

## Task 12: Dockerfile and Deployment Configuration

- [ ] 12.1 Create multi-stage Dockerfile: SDK image for build/publish, runtime image (aspnet:8.0-alpine), expose port 8080, set ASPNETCORE_URLS=http://+:8080
- [ ] 12.2 Create .dockerignore to exclude bin/, obj/, .git/, tests/
- [ ] 12.3 Create docker-compose.yml for local development with PostgreSQL 16 and Redis 7 containers, volume mount for migration script

## Task 13: Property-Based Tests (Critical Invariants)

- [ ]* 13.1 Write property test (Property 1): For any transaction amount A (positive decimal) and any valid rule percentage P (0 < P ≤ 100), CashbackCalculator.Calculate(A, rule) equals A × (P / 100m) exactly — no floating-point drift
- [ ]* 13.2 Write property test (Property 2): For any transfer amount X and daily total T where X + T > daily_limit, TransferValidator.Validate throws TransferLimitExceededException. For X + T ≤ daily_limit AND X ≤ per_transaction_limit, it succeeds
- [ ]* 13.3 Write property test (Property 3): For any completed write operation with Idempotency-Key K, calling GetAsync(K) returns the exact serialized response that was passed to SetAsync(K, response)
- [ ]* 13.4 Write property test (Property 4): For any CreditCashbackUseCase execution, the idempotency key passed to ILedgerClient.PostTransactionAsync is never equal to the client-provided key (Ledger key isolation)
- [ ]* 13.5 Write property test (Property 5): For any transaction amount outside [rule.MinAmount, rule.MaxAmount], CashbackCalculator.Calculate throws IneligibleCashbackException without calling ILedgerClient
- [ ]* 13.6 Write property test (Property 6): For any sequence of N approved transfers with amounts [a1..aN], SUM(a1..aN) ≤ daily_limit — TransferValidator enforces this invariant on every call
- [ ]* 13.7 Write property test (Property 7): For any pageSize in [1, 100], the clamping logic Math.Clamp(pageSize, 1, 100) always produces a value in [1, 100]

## Notes

- Tasks marked with `*` are property-based tests (optional for faster MVP, recommended for correctness)
- The Product Service depends on existing shared libraries (CorePoints.Resilience, CorePoints.Caching, CorePoints.FeatureToggles) — these are project references, not reimplemented
- The Outbox Worker is a separate service (already spec'd) that publishes outbox_events to SNS
- Ledger Core is a separate service — Product only interacts via ILedgerClient interface
- All monetary values use `decimal` in C# and DECIMAL(18,4) in PostgreSQL — float/double prohibited

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4", "1.5"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.3", "2.4", "2.5"] },
    { "id": 2, "tasks": ["2.6", "2.7", "3.1", "3.2", "3.3", "3.4", "3.5", "3.6", "3.7", "3.8"] },
    { "id": 3, "tasks": ["4.1", "4.2", "4.3", "4.4", "4.5", "4.6"] },
    { "id": 4, "tasks": ["4.7", "4.8", "5.1", "5.2", "5.3", "5.4", "5.5", "5.6"] },
    { "id": 5, "tasks": ["6.1", "6.2", "7.1", "7.2", "7.3", "7.4", "7.5"] },
    { "id": 6, "tasks": ["8.1", "8.2", "8.3", "8.4", "9.1", "9.2", "9.3"] },
    { "id": 7, "tasks": ["10.1", "10.2", "10.3", "10.4", "10.5", "10.6", "10.7"] },
    { "id": 8, "tasks": ["11.1", "11.2", "12.1", "12.2", "12.3"] },
    { "id": 9, "tasks": ["13.1", "13.2", "13.3", "13.4", "13.5", "13.6", "13.7"] }
  ]
}
```
