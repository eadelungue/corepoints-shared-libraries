-- Ledger Core Schema Migration
-- Version: 001
-- Description: Create initial ledger schema with accounts, transactions, outbox_events, and idempotency_keys tables

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
    correlation_id VARCHAR(200) NOT NULL,
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
