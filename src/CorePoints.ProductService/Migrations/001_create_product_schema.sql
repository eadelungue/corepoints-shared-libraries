-- Migration 001: Create Product Service schema
-- Run against the product_service database

BEGIN;

-- Cashback rules table
CREATE TABLE IF NOT EXISTS cashback_rules (
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

-- Transfer limits table
CREATE TABLE IF NOT EXISTS transfer_limits (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    account_type VARCHAR(50) NOT NULL UNIQUE,
    daily_limit DECIMAL(18, 4) NOT NULL CHECK (daily_limit > 0),
    per_transaction_limit DECIMAL(18, 4) NOT NULL CHECK (per_transaction_limit > 0),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Transfer history table
CREATE TABLE IF NOT EXISTS transfer_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_account_id UUID NOT NULL,
    destination_account_id UUID NOT NULL,
    amount DECIMAL(18, 4) NOT NULL CHECK (amount > 0),
    ledger_transaction_id UUID NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_transfer_history_source_date
    ON transfer_history(source_account_id, created_at DESC);

-- Outbox events table
CREATE TABLE IF NOT EXISTS outbox_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    correlation_id VARCHAR(100),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    published_at TIMESTAMP WITH TIME ZONE,
    retry_count INT NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_outbox_unpublished
    ON outbox_events(created_at) WHERE published_at IS NULL;

-- Idempotency keys table
CREATE TABLE IF NOT EXISTS idempotency_keys (
    key VARCHAR(100) PRIMARY KEY,
    response_payload JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_idempotency_expires ON idempotency_keys(expires_at);

-- Seed data: default cashback rules
INSERT INTO cashback_rules (name, percentage, min_amount, max_amount, is_active, target_groups) VALUES
    ('Standard Cashback', 2.00, 10.0000, 5000.0000, true, ARRAY['standard', 'premium']),
    ('Premium Cashback', 5.00, 50.0000, 10000.0000, true, ARRAY['premium']),
    ('Promotional Cashback', 3.50, 25.0000, 2500.0000, true, ARRAY['standard', 'premium', 'new_user'])
ON CONFLICT DO NOTHING;

-- Seed data: default transfer limits
INSERT INTO transfer_limits (account_type, daily_limit, per_transaction_limit) VALUES
    ('standard', 5000.0000, 1000.0000),
    ('premium', 50000.0000, 10000.0000),
    ('business', 250000.0000, 50000.0000)
ON CONFLICT (account_type) DO NOTHING;

COMMIT;
