-- ExitPass v1.2 local/testing QRPH routing override.
-- Use PayMongo for QRPH until AUB test API credentials are available.

UPDATE payments.payment_provider_routing_policies
SET
    primary_provider_code = 'PAYMONGO',
    fallback_provider_code = NULL,
    is_enabled = true,
    primary_provider_enabled = true,
    fallback_provider_enabled = false,
    updated_at = now(),
    row_version = row_version + 1
WHERE site_id IS NULL
  AND site_group_id IS NULL
  AND payment_method_code = 'QRPH'
  AND currency_code = 'PHP'
  AND min_amount_minor_units IS NULL
  AND max_amount_minor_units IS NULL
  AND (
      primary_provider_code <> 'PAYMONGO'
      OR fallback_provider_code IS NOT NULL
      OR is_enabled IS DISTINCT FROM true
      OR primary_provider_enabled IS DISTINCT FROM true
      OR fallback_provider_enabled IS DISTINCT FROM false
  );

UPDATE payments.payment_rails
SET
    rail_status = 'ACTIVE',
    updated_at = now(),
    row_version = row_version + 1
WHERE provider_code = 'PAYMONGO'
  AND rail_type = 'QRPH'
  AND rail_status IS DISTINCT FROM 'ACTIVE';
