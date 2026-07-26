# Requirements Document

## Introduction

The Ledger Core Service is the central accounting engine for CorePoints. It implements double-entry bookkeeping as a synchronous internal REST API deployed on ECS Fargate. It is accessed exclusively by Product services via AWS Cloud Map service discovery within the private VPC. The service uses C# .NET 8 with Minimal APIs, Dapper for PostgreSQL access via RDS Proxy, Redis for balance caching and idempotency checks, and implements the Transactional Outbox pattern for event notification.

## Glossary

- **Ledger_Core**: The C# .NET 8 Minimal API application that serves as the synchronous accounting engine
- **Account**: A ledger account entity with a unique ID, holder name, account type (DEBIT or CREDIT), and balance stored as DECIMAL(18,4)
- **Transaction**: A double-entry bookkeeping record linking a debit account, credit account, amount, and idempotency key
- **Outbox_Event**: A database record persisted in the same ACID transaction as the ledger entry, later published by a separate worker
- **Idempotency_Store**: A Redis-backed (with DB fallback) mechanism that stores previously processed idempotency keys and their responses
- **Balance_Cache**: A Redis cache-aside store for account balances with synchronous invalidation and short TTL (5-10 seconds)
- **Product_Service**: An upstream service that calls the Ledger Core synchronously via Cloud Map to execute accounting operations
- **RDS_Proxy**: AWS RDS Proxy providing connection pooling to the PostgreSQL database
- **Use_Case**: An application-layer class implementing a single business operation following Clean Architecture

## Requirements

### Requirement 1: Account Creation

**User Story:** As a Product Service, I want to create ledger accounts, so that I can assign debit and credit accounts to holders for financial operations.

#### Acceptance Criteria

1. WHEN a valid CreateAccountRequest is received at POST /accounts, THE Ledger_Core SHALL create an Account with a generated UUID, persist it to PostgreSQL, and return an AccountResponse with HTTP 201
2. THE Ledger_Core SHALL store the account balance as DECIMAL(18,4) with an initial value of zero
3. WHEN an invalid CreateAccountRequest is received (missing holder_name or invalid account_type), THE Ledger_Core SHALL return an HTTP 400 response following RFC 7807 Problem Details format
4. THE Ledger_Core SHALL accept only DEBIT or CREDIT as valid account_type values

### Requirement 2: Transaction Recording (Double-Entry Bookkeeping)

**User Story:** As a Product Service, I want to record financial transactions with guaranteed double-entry bookkeeping, so that every debit has a corresponding credit and account balances remain consistent.

#### Acceptance Criteria

1. WHEN a valid CreateTransactionRequest is received at POST /transactions with an Idempotency-Key header, THE Ledger_Core SHALL debit the source account and credit the destination account within a single ACID transaction
2. THE Ledger_Core SHALL use SELECT ... FOR UPDATE on both accounts to serialize concurrent transactions on the same account
3. THE Ledger_Core SHALL persist the Transaction record with debit_account_id, credit_account_id, amount as DECIMAL(18,4), description, and the idempotency_key
4. WHEN the transaction commits successfully, THE Ledger_Core SHALL persist an Outbox_Event in the same ACID transaction containing the event type, transaction ID, account IDs, amount, and correlation ID
5. WHEN the transaction commits successfully, THE Ledger_Core SHALL invalidate the Balance_Cache entries for both the debit and credit accounts synchronously within the same request
6. THE Ledger_Core SHALL return a TransactionResponse with the transaction ID, updated balances for both accounts, and HTTP 201 for new transactions
7. WHEN the debit account has insufficient balance for the requested amount, THE Ledger_Core SHALL reject the transaction and return HTTP 422 with a Problem Details response
8. WHEN either the debit_account_id or credit_account_id does not exist, THE Ledger_Core SHALL return HTTP 404 with a Problem Details response
9. THE Ledger_Core SHALL use only the decimal type in C# for all monetary calculations and storage — float and double are prohibited

### Requirement 3: Idempotency

**User Story:** As a Product Service, I want transaction operations to be idempotent, so that retries due to network failures do not cause duplicate financial entries.

#### Acceptance Criteria

1. WHEN a POST /transactions request is received, THE Ledger_Core SHALL require the Idempotency-Key header and return HTTP 400 if it is missing
2. WHEN an Idempotency-Key has already been processed, THE Ledger_Core SHALL return the original response payload with HTTP 200 without re-executing the transaction
3. WHEN an Idempotency-Key has not been processed, THE Ledger_Core SHALL store the key and its response payload in the Idempotency_Store after successful transaction commit
4. THE Ledger_Core SHALL check the Idempotency_Store in Redis first, falling back to the database idempotency_keys table if Redis is unavailable
5. THE Ledger_Core SHALL store idempotency records with an expiration time (TTL of 24 hours) to prevent unbounded storage growth

### Requirement 4: Balance Query with Cache

**User Story:** As a Product Service, I want to query account balances efficiently, so that I can make real-time decisions based on current account state.

#### Acceptance Criteria

1. WHEN a GET /accounts/{id}/balance request is received, THE Ledger_Core SHALL first check the Balance_Cache in Redis for the account balance
2. WHEN a cache hit occurs, THE Ledger_Core SHALL return the cached balance with HTTP 200
3. WHEN a cache miss occurs, THE Ledger_Core SHALL query the PostgreSQL database for the current balance, populate the Balance_Cache with a TTL of 5-10 seconds, and return the balance with HTTP 200
4. WHEN the account ID does not exist, THE Ledger_Core SHALL return HTTP 404 with a Problem Details response
5. THE Ledger_Core SHALL return the balance as a decimal value with up to 4 decimal places

### Requirement 5: Account Statement (Paginated)

**User Story:** As a Product Service, I want to retrieve paginated account statements, so that I can display transaction history without loading excessive data.

#### Acceptance Criteria

1. WHEN a GET /accounts/{id}/statement request is received, THE Ledger_Core SHALL return a paginated list of transactions involving the specified account ordered by created_at descending
2. THE Ledger_Core SHALL accept page and pageSize query parameters with defaults of page=1 and pageSize=20
3. THE Ledger_Core SHALL enforce a maximum pageSize of 100 records per page
4. THE Ledger_Core SHALL return a PaginatedStatementResponse containing the items, current page, page size, and total count with HTTP 200
5. WHEN the account ID does not exist, THE Ledger_Core SHALL return HTTP 404 with a Problem Details response

### Requirement 6: Transaction Query

**User Story:** As a Product Service, I want to retrieve transaction details by ID, so that I can verify the status and details of previously recorded transactions.

#### Acceptance Criteria

1. WHEN a GET /transactions/{id} request is received with a valid transaction ID, THE Ledger_Core SHALL return the TransactionResponse with HTTP 200
2. WHEN the transaction ID does not exist, THE Ledger_Core SHALL return HTTP 404 with a Problem Details response

### Requirement 7: Health Checks

**User Story:** As the infrastructure orchestrator (ECS), I want to probe liveness and readiness of the Ledger Core, so that unhealthy instances are replaced and traffic is routed only to healthy ones.

#### Acceptance Criteria

1. WHEN a GET /health/live request is received, THE Ledger_Core SHALL return HTTP 200 without checking any external dependency
2. WHEN a GET /health/ready request is received, THE Ledger_Core SHALL verify connectivity to PostgreSQL (via RDS_Proxy) and Redis, returning HTTP 200 if all dependencies are reachable
3. IF PostgreSQL or Redis is unreachable during a readiness check, THEN THE Ledger_Core SHALL return HTTP 503 with a JSON payload listing the status of each dependency

### Requirement 8: Resilience and Connection Management

**User Story:** As the platform team, I want the Ledger Core to handle transient failures gracefully, so that temporary infrastructure issues do not cause cascading failures.

#### Acceptance Criteria

1. THE Ledger_Core SHALL use Polly retry policies with exponential backoff and jitter for all PostgreSQL operations via Dapper
2. THE Ledger_Core SHALL connect to PostgreSQL exclusively through RDS_Proxy for connection pooling
3. THE Ledger_Core SHALL propagate CancellationToken through all async method calls from endpoint handlers to database and Redis operations
4. IF a Redis connection failure occurs during balance cache read, THEN THE Ledger_Core SHALL fall back to querying PostgreSQL directly without returning an error to the caller

### Requirement 9: Observability

**User Story:** As the platform team, I want structured logging and distributed tracing in the Ledger Core, so that I can correlate requests across services and diagnose issues quickly.

#### Acceptance Criteria

1. THE Ledger_Core SHALL emit structured JSON logs via Serilog enriched with TraceId, SpanId, and X-Correlation-ID
2. THE Ledger_Core SHALL propagate W3C Trace Context headers (traceparent, tracestate) on all outgoing operations
3. THE Ledger_Core SHALL export telemetry data (traces, metrics, logs) via OTLP protocol
4. THE Ledger_Core SHALL log transaction operations with account IDs, amounts, and idempotency keys without including any PII

### Requirement 10: API Contract and Error Handling

**User Story:** As a Product Service developer, I want consistent and well-documented API responses, so that I can integrate with the Ledger Core reliably.

#### Acceptance Criteria

1. THE Ledger_Core SHALL expose OpenAPI/Swagger documentation at a configured endpoint for internal use
2. THE Ledger_Core SHALL return all error responses in RFC 7807 Problem Details format with type, title, status, and detail fields
3. THE Ledger_Core SHALL never expose stack traces or internal implementation details in error responses
4. WHEN an unhandled exception occurs, THE Ledger_Core SHALL return HTTP 500 with a generic Problem Details response and log the full exception details internally
