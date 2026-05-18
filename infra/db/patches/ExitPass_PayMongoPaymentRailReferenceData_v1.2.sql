-- ExitPass v1.2 PayMongo payment rail reference data.
-- Ensures Payment Orchestrator provider-session persistence can resolve PayMongo Checkout Session by rail_code.

UPDATE payments.payment_rails
SET
    rail_name = 'PayMongo Checkout Session',
    provider_code = 'PAYMONGO',
    rail_type = 'HOSTED_CHECKOUT',
    supported_currency_code = 'PHP',
    rail_status = 'ACTIVE',
    is_primary = true,
    is_fallback = false,
    effective_from = now() - interval '1 day',
    effective_to = NULL,
    updated_at = now(),
    row_version = 1
WHERE rail_code = 'PAYMONGO_CHECKOUT_SESSION';

INSERT INTO payments.payment_rails (
    payment_rail_id,
    rail_code,
    rail_name,
    provider_code,
    rail_type,
    supported_currency_code,
    rail_status,
    is_primary,
    is_fallback,
    effective_from,
    effective_to,
    created_at,
    updated_at,
    row_version
)
SELECT
    '12000000-0000-0000-0000-000000000205',
    'PAYMONGO_CHECKOUT_SESSION',
    'PayMongo Checkout Session',
    'PAYMONGO',
    'HOSTED_CHECKOUT',
    'PHP',
    'ACTIVE',
    true,
    false,
    now() - interval '1 day',
    NULL,
    now(),
    now(),
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM payments.payment_rails
    WHERE rail_code = 'PAYMONGO_CHECKOUT_SESSION'
);
