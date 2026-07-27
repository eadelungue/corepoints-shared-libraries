# Requirements Document

## Introduction

The Product Service is the public-facing business logic layer for CorePoints. It exposes REST endpoints via API Gateway with JWT (Cognito) authorization, implements domain rules (cashback calculation, transfer limits, eligibility checks), and orchestrates calls to the Ledger Core for all accounting operations. The service uses C# .NET 8 Minimal APIs, Dapper for its own PostgreSQL database, the shared CorePoints.Resilience library (ILedgerClient) for Ledger communication, CorePoints.Caching for Redis, and the Transactional Outbox pattern for domain event publication.

## Glossary

- **Product_Service**: The C# .NET 8 Minimal API application that implements CorePoints business rules and exposes public endpoints via API Gateway
- **Ledger_Core**: The internal synchronous accounting engine accessed via Cloud Map HTTP (never directly exposed to clients)
- **ILedgerClient**: The typed HttpClient interface from CorePoints.Resilience used for all Ledger communication, with built-in Polly policies
- **Cashback_Rule**: A database record defining cashback percentage, min/max transaction amounts, active status, and target account groups
- **Transfer_Limit**: A database record defining daily and per-transaction point transfer limits by account type
- **Outbox_Event**: A database record persisted in the Product database after Ledger confirmation, later published to SNS by the Outbox Worker
- **Idempotency_Store**: A Redis-backed (with DB fallback) mechanism storing processed idempotency keys and their responses, scoped to external client keys
- **Feature_Toggle**: A CorePoints.FeatureToggles flag that gates specific business features (e.g., cashback, transfers)
- **Correlation_ID**: The X-Correlation-ID header propagated across all service boundaries for distributed tracing
- **JWT_Claims**: The authenticated user identity and attributes extracted from the Cognito JWT token available in HttpContext.User

## Requirements

### Requirement 1: Cashback Credit

**User Story:** As an external client, I want to calculate and credit cashback to a user account, so that eligible transactions earn points according to configured business rules.

#### Acceptance Criteria

1. WHEN a valid POST /api/v1/cashback/credit request is received with an Idempotency-Key header, THE Product_Service SHALL validate the request against active Cashback_Rules (percentage, min_amount, max_amount, target_groups)
2. WHEN the transaction amount is below the Cashback_Rule min_amount or above max_amount, THE Product_Service SHALL reject the request with HTTP 422 and a Problem Details response indicating ineligibility
3. WHEN the account is not in an eligible target_group for the matched Cashback_Rule, THE Product_Service SHALL reject the request with HTTP 422 and a Problem Details response
4. WHEN cashback eligibility is confirmed, THE Product_Service SHALL calculate the cashback amount as transaction_amount multiplied by the rule percentage, using decimal arithmetic exclusively
5. WHEN cashback is calculated, THE Product_Service SHALL call ILedgerClient.PostTransactionAsync with a generated Idempotency-Key, the Correlation_ID, credit to the user account and debit from the system cashback source account
6. WHEN the Ledger confirms the transaction, THE Product_Service SHALL persist an Outbox_Event of type CashbackCredited containing the transaction ID, account ID, cashback amount, and original transaction reference
7. THE Product_Service SHALL return HTTP 201 with the cashback transaction details including the calculated amount and Ledger transaction ID
8. WHILE the cashback Feature_Toggle is disabled, THE Product_Service SHALL return HTTP 503 with a Problem Details response indicating feature unavailable

### Requirement 2: Point Transfer

**User Story:** As an external client, I want to transfer points between accounts, so that users can share or move their balances.

#### Acceptance Criteria

1. WHEN a valid POST /api/v1/transfers request is received with an Idempotency-Key header, THE Product_Service SHALL validate the transfer against Transfer_Limit rules (daily_limit, per_transaction_limit) for the source account type
2. WHEN the transfer amount exceeds the per_transaction_limit for the source account type, THE Product_Service SHALL reject the request with HTTP 422 and a Problem Details response
3. WHEN the source account has exceeded the daily_limit (sum of transfers today), THE Product_Service SHALL reject the request with HTTP 422 and a Problem Details response indicating daily limit reached
4. WHEN transfer limits are satisfied, THE Product_Service SHALL call ILedgerClient.PostTransactionAsync with a generated Idempotency-Key, the Correlation_ID, debit from source and credit to destination
5. WHEN the Ledger confirms the transaction, THE Product_Service SHALL persist an Outbox_Event of type TransferCompleted containing the transaction ID, source account, destination account, and amount
6. THE Product_Service SHALL return HTTP 201 with the transfer details including the Ledger transaction ID
7. WHEN the Ledger returns HTTP 422 (insufficient balance), THE Product_Service SHALL return HTTP 422 to the client with a Problem Details response indicating insufficient balance
8. WHILE the transfers Feature_Toggle is disabled, THE Product_Service SHALL return HTTP 503 with a Problem Details response indicating feature unavailable

### Requirement 3: Balance Inquiry (Ledger Proxy)

**User Story:** As an external client, I want to check an account balance, so that I can display real-time point balances.

#### Acceptance Criteria

1. WHEN a GET /api/v1/accounts/{id}/balance request is received, THE Product_Service SHALL verify the authenticated user (JWT_Claims) owns or has access to the requested account
2. WHEN the user is authorized, THE Product_Service SHALL call ILedgerClient.GetBalanceAsync with the account ID and Correlation_ID
3. THE Product_Service SHALL return the Ledger balance response with HTTP 200 formatted in the Product API contract (not raw Ledger DTOs)
4. WHEN the Ledger returns HTTP 404, THE Product_Service SHALL return HTTP 404 with a Problem Details response
5. THE Product_Service SHALL cache balance responses via CorePoints.Caching with a short TTL (5 seconds) keyed by account ID

### Requirement 4: Account Statement (Ledger Proxy)

**User Story:** As an external client, I want to view paginated account statements, so that I can review transaction history.

#### Acceptance Criteria

1. WHEN a GET /api/v1/accounts/{id}/statement request is received, THE Product_Service SHALL verify the authenticated user (JWT_Claims) owns or has access to the requested account
2. WHEN the user is authorized, THE Product_Service SHALL call ILedgerClient.GetStatementAsync with the account ID and Correlation_ID
3. THE Product_Service SHALL accept page and pageSize query parameters, applying defaults of page=1 and pageSize=20 with a maximum pageSize of 100
4. THE Product_Service SHALL return the paginated statement formatted in the Product API contract with HTTP 200
5. WHEN the Ledger returns HTTP 404, THE Product_Service SHALL return HTTP 404 with a Problem Details response

### Requirement 5: Transaction Detail Query

**User Story:** As an external client, I want to view details of a specific transaction, so that I can verify transaction status and information.

#### Acceptance Criteria

1. WHEN a GET /api/v1/transactions/{id} request is received, THE Product_Service SHALL call ILedgerClient.GetTransactionAsync with the transaction ID and Correlation_ID
2. THE Product_Service SHALL return the transaction details formatted in the Product API contract with HTTP 200
3. WHEN the Ledger returns HTTP 404, THE Product_Service SHALL return HTTP 404 with a Problem Details response

### Requirement 6: External Client Idempotency

**User Story:** As the platform team, I want the Product Service to validate Idempotency-Key headers from external clients, so that client retries do not produce duplicate business operations.

#### Acceptance Criteria

1. WHEN a write endpoint (POST /api/v1/cashback/credit or POST /api/v1/transfers) is called without an Idempotency-Key header, THE Product_Service SHALL return HTTP 400 with a Problem Details response
2. WHEN an Idempotency-Key has already been processed by the Product_Service, THE Product_Service SHALL return the original response payload with HTTP 200 without re-executing any business logic or calling the Ledger
3. WHEN an Idempotency-Key has not been processed, THE Product_Service SHALL store the key and its response in the Idempotency_Store after successful Ledger confirmation and outbox persistence
4. THE Product_Service SHALL check the Idempotency_Store in Redis first, falling back to the database idempotency_keys table if Redis is unavailable
5. THE Product_Service SHALL store idempotency records with a TTL of 24 hours
6. THE Product_Service SHALL generate a new unique Idempotency-Key for each call to ILedgerClient.PostTransactionAsync, distinct from the client-provided key

### Requirement 7: Health Checks

**User Story:** As the infrastructure orchestrator (ECS), I want to probe liveness and readiness of the Product Service, so that unhealthy instances are replaced and traffic is routed to healthy ones.

#### Acceptance Criteria

1. WHEN a GET /health/live request is received, THE Product_Service SHALL return HTTP 200 without checking any external dependency
2. WHEN a GET /health/ready request is received, THE Product_Service SHALL verify connectivity to its PostgreSQL database, Redis, and the Ledger_Core (via ILedgerClient health or a lightweight call), returning HTTP 200 if all dependencies are reachable
3. IF any dependency is unreachable during a readiness check, THEN THE Product_Service SHALL return HTTP 503 with a JSON payload listing the status of each dependency

### Requirement 8: Authorization and Access Control

**User Story:** As the security team, I want the Product Service to enforce JWT-based authorization, so that only authenticated users can access their own data.

#### Acceptance Criteria

1. THE Product_Service SHALL extract and validate JWT_Claims from HttpContext.User for all protected endpoints (API Gateway handles token validation, Product validates claims)
2. WHEN a request to GET /api/v1/accounts/{id}/balance or GET /api/v1/accounts/{id}/statement targets an account not owned by the authenticated user, THE Product_Service SHALL return HTTP 403
3. THE Product_Service SHALL extract the user identifier (sub claim) from JWT_Claims to determine account ownership
4. THE Product_Service SHALL allow health check endpoints (/health/live, /health/ready) without authentication

### Requirement 9: Observability

**User Story:** As the platform team, I want structured logging and distributed tracing in the Product Service, so that I can correlate requests across API Gateway, Product, and Ledger.

#### Acceptance Criteria

1. THE Product_Service SHALL emit structured JSON logs via Serilog enriched with TraceId, SpanId, and X-Correlation-ID
2. THE Product_Service SHALL propagate X-Correlation-ID on all outgoing calls to ILedgerClient
3. THE Product_Service SHALL export telemetry data (traces, metrics, logs) via OTLP protocol
4. THE Product_Service SHALL log business operations (cashback calculations, transfer validations) with relevant IDs and amounts without including PII

### Requirement 10: Error Handling and API Contract

**User Story:** As an external client developer, I want consistent and well-documented error responses, so that I can integrate with the Product Service reliably.

#### Acceptance Criteria

1. THE Product_Service SHALL return all error responses in RFC 7807 Problem Details format with type, title, status, and detail fields
2. THE Product_Service SHALL never expose stack traces or internal implementation details in error responses
3. WHEN an unhandled exception occurs, THE Product_Service SHALL return HTTP 500 with a generic Problem Details response and log the full exception details internally
4. WHEN the Ledger_Core returns an error, THE Product_Service SHALL map it to an appropriate client-facing error (not exposing internal Ledger details)
5. THE Product_Service SHALL expose OpenAPI/Swagger documentation at a configured endpoint

### Requirement 11: Resilience

**User Story:** As the platform team, I want the Product Service to handle transient Ledger failures gracefully, so that temporary infrastructure issues do not cascade to external clients.

#### Acceptance Criteria

1. THE Product_Service SHALL use ILedgerClient (CorePoints.Resilience) which provides Polly retry with exponential backoff + jitter and circuit breaker for all Ledger calls
2. WHEN the circuit breaker for Ledger communication is open, THE Product_Service SHALL return HTTP 503 to the client with a Problem Details response indicating temporary unavailability
3. THE Product_Service SHALL propagate CancellationToken through all async method calls from endpoint handlers to Ledger calls and database operations
4. IF a Redis connection failure occurs during idempotency or cache operations, THEN THE Product_Service SHALL fall back gracefully (DB fallback for idempotency, skip cache for reads) without returning an error to the caller
