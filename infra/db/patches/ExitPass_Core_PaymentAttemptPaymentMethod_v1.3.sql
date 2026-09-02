/*
 * Persists the customer-selected payment method independently of the provider rail.
 * Existing attempts remain readable without exposing their internal provider rail as a customer method.
 */

ALTER TABLE core.payment_attempts
    ADD COLUMN IF NOT EXISTS payment_method_code varchar(32);

COMMENT ON COLUMN core.payment_attempts.payment_method_code IS
    'Customer-selected payment method kept separate from the internal provider rail.';
