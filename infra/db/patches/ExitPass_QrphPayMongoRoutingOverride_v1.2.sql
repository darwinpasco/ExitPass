-- ExitPass v1.2 local/testing WebPay routing override.
-- WebPay QRPH, GCash, Maya, and Card PHP payments must route to PayMongo
-- for the current integration slice. AUB must not be configured as primary
-- or fallback until the AUB integration slice is explicitly started.

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
  AND payment_method_code IN ('QRPH', 'GCASH', 'MAYA', 'CARD')
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
