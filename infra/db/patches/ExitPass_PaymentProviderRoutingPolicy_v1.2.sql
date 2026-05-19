-- ExitPass v1.2 payment provider routing policy baseline.
-- Routing is a business/commercial policy owned by Payment Orchestrator provider selection.

CREATE TABLE IF NOT EXISTS payments.payment_provider_routing_policies (
    payment_routing_policy_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_id uuid NULL,
    site_group_id uuid NULL,
    payment_method_code varchar(32) NOT NULL,
    primary_provider_code varchar(64) NOT NULL,
    fallback_provider_code varchar(64) NULL,
    currency_code char(3) NOT NULL,
    min_amount_minor_units bigint NULL,
    max_amount_minor_units bigint NULL,
    is_enabled boolean DEFAULT true NOT NULL,
    primary_provider_enabled boolean DEFAULT true NOT NULL,
    fallback_provider_enabled boolean DEFAULT true NOT NULL,
    effective_from timestamptz DEFAULT now() NOT NULL,
    effective_until timestamptz NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_payment_provider_routing_policies PRIMARY KEY (payment_routing_policy_id),
    CONSTRAINT ck_payment_provider_routing_policies__amount_bounds CHECK (
        min_amount_minor_units IS NULL
        OR max_amount_minor_units IS NULL
        OR max_amount_minor_units >= min_amount_minor_units
    ),
    CONSTRAINT ck_payment_provider_routing_policies__effective_window CHECK (
        effective_until IS NULL OR effective_until > effective_from
    ),
    CONSTRAINT ck_payment_provider_routing_policies__row_version_positive CHECK (row_version > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_payment_provider_routing_policies__default_method_currency
    ON payments.payment_provider_routing_policies (payment_method_code, currency_code)
    WHERE site_id IS NULL
      AND site_group_id IS NULL
      AND min_amount_minor_units IS NULL
      AND max_amount_minor_units IS NULL;

CREATE INDEX IF NOT EXISTS ix_payment_provider_routing_policies__lookup
    ON payments.payment_provider_routing_policies (
        payment_method_code,
        currency_code,
        is_enabled,
        effective_from,
        effective_until
    );

COMMENT ON TABLE payments.payment_provider_routing_policies IS 'Payment provider routing policy for Payment Orchestrator provider selection.';
COMMENT ON COLUMN payments.payment_provider_routing_policies.payment_method_code IS 'Customer-selected payment method code, for example QRPH, CARD, GCASH, or MAYA.';
COMMENT ON COLUMN payments.payment_provider_routing_policies.primary_provider_code IS 'Primary provider selected when no valid preferred provider is supplied.';
COMMENT ON COLUMN payments.payment_provider_routing_policies.fallback_provider_code IS 'Fallback provider selected when the primary provider is disabled and fallback is enabled.';
COMMENT ON COLUMN payments.payment_provider_routing_policies.primary_provider_enabled IS 'Controls whether the configured primary provider is selectable.';
COMMENT ON COLUMN payments.payment_provider_routing_policies.fallback_provider_enabled IS 'Controls whether the configured fallback provider is selectable.';

INSERT INTO payments.payment_provider_routing_policies (
    payment_routing_policy_id,
    payment_method_code,
    primary_provider_code,
    fallback_provider_code,
    currency_code,
    is_enabled,
    primary_provider_enabled,
    fallback_provider_enabled,
    effective_from
)
VALUES
    ('12000000-0000-0000-0000-000000000301', 'QRPH', 'AUB', 'PAYMONGO', 'PHP', true, true, true, '2026-01-01T00:00:00Z'),
    ('12000000-0000-0000-0000-000000000302', 'CARD', 'AUB', 'PAYMONGO', 'PHP', true, true, true, '2026-01-01T00:00:00Z'),
    ('12000000-0000-0000-0000-000000000303', 'GCASH', 'PAYMONGO', NULL, 'PHP', true, true, false, '2026-01-01T00:00:00Z'),
    ('12000000-0000-0000-0000-000000000304', 'MAYA', 'PAYMONGO', NULL, 'PHP', true, true, false, '2026-01-01T00:00:00Z')
ON CONFLICT ON CONSTRAINT pk_payment_provider_routing_policies DO UPDATE
SET
    payment_method_code = EXCLUDED.payment_method_code,
    primary_provider_code = EXCLUDED.primary_provider_code,
    fallback_provider_code = EXCLUDED.fallback_provider_code,
    currency_code = EXCLUDED.currency_code,
    is_enabled = EXCLUDED.is_enabled,
    primary_provider_enabled = EXCLUDED.primary_provider_enabled,
    fallback_provider_enabled = EXCLUDED.fallback_provider_enabled,
    effective_from = EXCLUDED.effective_from,
    updated_at = now(),
    row_version = payments.payment_provider_routing_policies.row_version + 1;
