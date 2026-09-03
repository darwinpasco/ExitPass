\set ON_ERROR_STOP on

BEGIN;

ALTER TABLE ist_configuration.site_operational_capabilities
    ADD COLUMN IF NOT EXISTS operator_entity_code text NULL,
    ADD COLUMN IF NOT EXISTS hikcentral_instance_code text NULL,
    ADD COLUMN IF NOT EXISTS hikcentral_parking_lot_index_code text NULL,
    ADD COLUMN IF NOT EXISTS hikcentral_parking_lot_name text NULL,
    ADD COLUMN IF NOT EXISTS pos_site_server_id uuid NULL,
    ADD COLUMN IF NOT EXISTS fiscal_identity_id uuid NULL,
    ADD COLUMN IF NOT EXISTS sales_invoice_profile_id uuid NULL;

DO $$
BEGIN
    IF (SELECT count(*) FROM ep_ist_operational) <> 46 THEN
        RAISE EXCEPTION 'Operational configuration requires exactly 46 real-Site rows.';
    END IF;
    IF EXISTS (SELECT 1 FROM ep_ist_operational GROUP BY site_id HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM ep_ist_operational GROUP BY site_code HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Operational configuration contains duplicate Site identity.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_operational input
        LEFT JOIN ist_configuration.real_site_catalog_members member
          ON member.site_id = input.site_id AND member.site_code = input.site_code
        WHERE member.site_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Operational configuration contains an unknown or conflicting Site identity.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM ep_ist_operational
        WHERE hikcentral_connectivity_verified AND NOT hikcentral_target_configured
           OR fiscal_profile_approved AND NOT (fiscal_merchant_configured AND fiscal_supplier_configured)
           OR fiscal_profile_approved AND (sales_invoice_profile_id IS NULL OR pos_site_server_id IS NULL OR fiscal_identity_id IS NULL)
           OR hikcentral_target_configured AND (hikcentral_instance_code IS NULL OR hikcentral_parking_lot_index_code IS NULL)
    ) THEN
        RAISE EXCEPTION 'Operational configuration contains an invalid capability relationship.';
    END IF;
END $$;

UPDATE sites.sites site
SET public_lookup_enabled = input.webpay_public_lookup_enabled,
    payment_enabled = input.webpay_payment_enabled,
    updated_at = now(),
    row_version = CASE
        WHEN site.public_lookup_enabled = input.webpay_public_lookup_enabled
         AND site.payment_enabled = input.webpay_payment_enabled THEN site.row_version
        ELSE site.row_version + 1
    END
FROM ep_ist_operational input
WHERE site.site_id = input.site_id
  AND (site.public_lookup_enabled IS DISTINCT FROM input.webpay_public_lookup_enabled
       OR site.payment_enabled IS DISTINCT FROM input.webpay_payment_enabled);

UPDATE ist_configuration.site_operational_capabilities capability
SET operator_entity_code = input.operator_entity_code,
    hikcentral_instance_code = input.hikcentral_instance_code,
    hikcentral_parking_lot_index_code = input.hikcentral_parking_lot_index_code,
    hikcentral_parking_lot_name = input.hikcentral_parking_lot_name,
    hikcentral_target_configured = input.hikcentral_target_configured,
    hikcentral_connectivity_verified = input.hikcentral_connectivity_verified,
    fiscal_merchant_configured = input.fiscal_merchant_configured,
    fiscal_supplier_configured = input.fiscal_supplier_configured,
    fiscal_profile_approved = input.fiscal_profile_approved,
    paymongo_enabled = input.paymongo_enabled,
    pos_site_server_id = input.pos_site_server_id,
    fiscal_identity_id = input.fiscal_identity_id,
    sales_invoice_profile_id = input.sales_invoice_profile_id,
    last_verified_at = input.last_verified_at,
    verification_reference = input.verification_reference
FROM ep_ist_operational input
WHERE capability.site_id = input.site_id;

CREATE OR REPLACE VIEW ist_configuration.real_site_operational_readiness AS
SELECT readiness.site_id,
       readiness.site_code,
       readiness.site_name,
       readiness.site_group_id,
       readiness.site_group_name,
       readiness.jurisdiction,
       readiness.senior_policy_status,
       readiness.pwd_policy_status,
       capability.operator_entity_code,
       capability.hikcentral_instance_code,
       capability.hikcentral_target_configured,
       capability.hikcentral_connectivity_verified,
       capability.hikcentral_parking_lot_index_code,
       capability.hikcentral_parking_lot_name,
       readiness.webpay_public_lookup_enabled,
       readiness.webpay_payment_enabled,
       capability.fiscal_merchant_configured,
       capability.fiscal_supplier_configured,
       capability.fiscal_profile_approved,
       capability.pos_site_server_id,
       capability.fiscal_identity_id,
       capability.sales_invoice_profile_id,
       capability.paymongo_enabled,
       readiness.final_test_readiness,
       capability.last_verified_at,
       capability.verification_reference
FROM ist_configuration.real_site_readiness readiness
JOIN ist_configuration.site_operational_capabilities capability USING (site_id);

COMMIT;
