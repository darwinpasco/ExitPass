-- ExitPass v1.3 Operator Console assisted customer intake foundation.
--
-- Establishes one editable parking-session Sales Invoice customer-information
-- authority, preserves payment-attempt information as an immutable snapshot,
-- and admits Operator Console submissions into the existing statutory review
-- projection. This migration deliberately performs no historical data backfill.

DO $preflight$
DECLARE
    site_operator_count integer;
BEGIN
    IF to_regclass('core.parking_sessions') IS NULL OR
       to_regclass('core.payment_attempts') IS NULL OR
       to_regclass('core.tariff_snapshots') IS NULL OR
       to_regclass('operator_console.statutory_discount_service_channel_reviews') IS NULL OR
       to_regclass('discounts.statutory_discount_validations') IS NULL OR
       to_regclass('identity.users') IS NULL OR
       to_regclass('identity.service_identities') IS NULL OR
       to_regclass('identity.permissions') IS NULL OR
       to_regclass('identity.roles') IS NULL OR
       to_regclass('identity.role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Operator Console assisted-intake migration prerequisites are missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM identity.service_identities
        WHERE service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
          AND identity_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'Canonical Central PMS migration service identity is unavailable';
    END IF;

    SELECT count(*) INTO site_operator_count
    FROM identity.roles
    WHERE role_code = 'SITE_OPERATOR'
      AND role_status = 'ACTIVE'
      AND role_provenance = 'CANONICAL_ROLE'
      AND human_assignable;

    IF site_operator_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one active, canonical, human-assignable SITE_OPERATOR role; found %', site_operator_count;
    END IF;
END
$preflight$;

BEGIN;
SET CONSTRAINTS ALL DEFERRED;

CREATE TABLE IF NOT EXISTS core.parking_session_invoice_customer_information (
    parking_session_id uuid NOT NULL,
    customer_name varchar(160),
    customer_address varchar(300),
    customer_tin varchar(40),
    business_style varchar(160),
    row_version bigint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    created_source_channel varchar(64) NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    updated_source_channel varchar(64) NOT NULL,
    CONSTRAINT pk_parking_session_invoice_customer_information PRIMARY KEY (parking_session_id),
    CONSTRAINT fk_ps_invoice_customer_information__parking_session FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id),
    CONSTRAINT fk_ps_invoice_customer_information__created_user FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_ps_invoice_customer_information__created_service FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT fk_ps_invoice_customer_information__updated_user FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_ps_invoice_customer_information__updated_service FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_ps_invoice_customer_information__has_value CHECK (num_nonnulls(customer_name, customer_address, customer_tin, business_style) > 0),
    CONSTRAINT ck_ps_invoice_customer_information__customer_name CHECK (customer_name IS NULL OR (customer_name = btrim(customer_name) AND customer_name <> '')),
    CONSTRAINT ck_ps_invoice_customer_information__customer_address CHECK (customer_address IS NULL OR (customer_address = btrim(customer_address) AND customer_address <> '')),
    CONSTRAINT ck_ps_invoice_customer_information__customer_tin CHECK (customer_tin IS NULL OR (customer_tin = btrim(customer_tin) AND customer_tin <> '')),
    CONSTRAINT ck_ps_invoice_customer_information__business_style CHECK (business_style IS NULL OR (business_style = btrim(business_style) AND business_style <> '')),
    CONSTRAINT ck_ps_invoice_customer_information__row_version CHECK (row_version > 0),
    CONSTRAINT ck_ps_invoice_customer_information__created_actor CHECK (num_nonnulls(created_by_user_id, created_by_service_identity_id) = 1),
    CONSTRAINT ck_ps_invoice_customer_information__updated_actor CHECK (num_nonnulls(updated_by_user_id, updated_by_service_identity_id) = 1),
    CONSTRAINT ck_ps_invoice_customer_information__created_source CHECK (created_source_channel IN ('OPERATOR_CONSOLE', 'WEBPAY', 'ASSISTED_PAYMENT_TERMINAL')),
    CONSTRAINT ck_ps_invoice_customer_information__updated_source CHECK (updated_source_channel IN ('OPERATOR_CONSOLE', 'WEBPAY', 'ASSISTED_PAYMENT_TERMINAL')),
    CONSTRAINT ck_ps_invoice_customer_information__timestamps CHECK (updated_at >= created_at)
);

COMMENT ON TABLE core.parking_session_invoice_customer_information IS
    'Central PMS authority for editable pre-issuance Sales Invoice customer information associated with one parking session. Issued fiscal documents use an immutable copied snapshot.';
COMMENT ON COLUMN core.parking_session_invoice_customer_information.row_version IS
    'Optimistic-concurrency version. Identical writes do not advance this value; accepted material changes advance it exactly once.';
COMMENT ON COLUMN core.parking_session_invoice_customer_information.created_source_channel IS
    'Server-derived originating channel. Browser-authored channel authority is prohibited.';
COMMENT ON COLUMN core.parking_session_invoice_customer_information.updated_source_channel IS
    'Server-derived channel responsible for the latest accepted material change.';

CREATE TABLE IF NOT EXISTS core.payment_attempt_invoice_customer_information (
    payment_attempt_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    tariff_snapshot_id uuid NOT NULL,
    customer_name varchar(160),
    customer_address varchar(300),
    customer_tin varchar(40),
    business_style varchar(160),
    statutory_id_number varchar(80),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_payment_attempt_invoice_customer_information PRIMARY KEY (payment_attempt_id),
    CONSTRAINT fk_payment_attempt_invoice_customer_information__attempt FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id),
    CONSTRAINT fk_payment_attempt_invoice_customer_information__session FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id),
    CONSTRAINT fk_payment_attempt_invoice_customer_information__tariff FOREIGN KEY (tariff_snapshot_id) REFERENCES core.tariff_snapshots(tariff_snapshot_id)
);

COMMENT ON TABLE core.payment_attempt_invoice_customer_information IS
    'Immutable Sales Invoice customer-information snapshot associated with one canonical payment attempt for fiscal issuance and replay. It is not the editable parking-session authority.';

ALTER TABLE operator_console.statutory_discount_service_channel_reviews
    ADD COLUMN IF NOT EXISTS birth_date date,
    ADD COLUMN IF NOT EXISTS submitted_by_user_id uuid;

ALTER TABLE discounts.statutory_discount_validations
    ADD COLUMN IF NOT EXISTS birth_date date;

DO $statutory_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'operator_console.statutory_discount_service_channel_reviews'::regclass
          AND conname = 'ck_stat_disc_svc_reviews__source_channel'
          AND pg_get_constraintdef(oid) LIKE '%OPERATOR_CONSOLE%'
    ) THEN
        ALTER TABLE operator_console.statutory_discount_service_channel_reviews
            DROP CONSTRAINT IF EXISTS ck_stat_disc_svc_reviews__source_channel;
        ALTER TABLE operator_console.statutory_discount_service_channel_reviews
            ADD CONSTRAINT ck_stat_disc_svc_reviews__source_channel
            CHECK (source_channel IN ('WEBPAY', 'ASSISTED_PAYMENT_TERMINAL', 'OPERATOR_CONSOLE'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stat_disc_svc_reviews__submitted_user') THEN
        ALTER TABLE operator_console.statutory_discount_service_channel_reviews
            ADD CONSTRAINT fk_stat_disc_svc_reviews__submitted_user
            FOREIGN KEY (submitted_by_user_id) REFERENCES identity.users(user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_stat_disc_svc_reviews__operator_submitter') THEN
        ALTER TABLE operator_console.statutory_discount_service_channel_reviews
            ADD CONSTRAINT ck_stat_disc_svc_reviews__operator_submitter CHECK (
                (source_channel = 'OPERATOR_CONSOLE' AND submitted_by_user_id IS NOT NULL)
                OR
                (source_channel IN ('WEBPAY', 'ASSISTED_PAYMENT_TERMINAL') AND submitted_by_user_id IS NULL));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_stat_disc_svc_reviews__operator_birth_date') THEN
        ALTER TABLE operator_console.statutory_discount_service_channel_reviews
            ADD CONSTRAINT ck_stat_disc_svc_reviews__operator_birth_date
            CHECK (source_channel <> 'OPERATOR_CONSOLE' OR birth_date IS NOT NULL);
    END IF;
END
$statutory_constraints$;

CREATE OR REPLACE FUNCTION pg_temp.exitpass_assisted_intake_uuid(input text)
RETURNS uuid LANGUAGE sql IMMUTABLE AS $$
    SELECT (substr(md5(input),1,8)||'-'||substr(md5(input),9,4)||'-'||substr(md5(input),13,4)||'-'||substr(md5(input),17,4)||'-'||substr(md5(input),21,12))::uuid
$$;

CREATE TEMP TABLE assisted_intake_permissions (
    permission_code varchar(96) PRIMARY KEY,
    permission_name varchar(128) NOT NULL,
    permission_description text NOT NULL,
    permission_domain varchar(64) NOT NULL,
    permission_action varchar(64) NOT NULL
) ON COMMIT DROP;

INSERT INTO assisted_intake_permissions VALUES
('statutory-discounts.decision.submit.operator-console','Submit Operator Console statutory request','Submit statutory review facts from an authorized Operator Console session.','statutory-discounts','submit'),
('statutory-discounts.decision.read','Read statutory request status','Read canonical statutory request status.','statutory-discounts','read'),
('sales-invoice-customer-information.read','Read Sales Invoice customer information','Read parking-session Sales Invoice customer information before issuance.','sales-invoice-customer-information','read'),
('sales-invoice-customer-information.manage','Manage Sales Invoice customer information','Create or update parking-session Sales Invoice customer information before issuance.','sales-invoice-customer-information','manage');

INSERT INTO identity.permissions (
    permission_id, permission_code, permission_name, permission_description,
    permission_domain, permission_action, permission_status, is_sensitive, requires_audit,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT pg_temp.exitpass_assisted_intake_uuid('assisted-intake:permission:' || permission_code),
       permission_code, permission_name, permission_description, permission_domain,
       permission_action, 'ACTIVE', true, true,
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM assisted_intake_permissions
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE SET
    permission_name = EXCLUDED.permission_name,
    permission_description = EXCLUDED.permission_description,
    permission_domain = EXCLUDED.permission_domain,
    permission_action = EXCLUDED.permission_action,
    permission_status = 'ACTIVE',
    is_sensitive = true,
    requires_audit = true,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now(),
    row_version = identity.permissions.row_version + 1
WHERE (identity.permissions.permission_name, identity.permissions.permission_description,
       identity.permissions.permission_domain, identity.permissions.permission_action,
       identity.permissions.permission_status, identity.permissions.is_sensitive,
       identity.permissions.requires_audit)
  IS DISTINCT FROM
      (EXCLUDED.permission_name, EXCLUDED.permission_description,
       EXCLUDED.permission_domain, EXCLUDED.permission_action,
       EXCLUDED.permission_status, EXCLUDED.is_sensitive, EXCLUDED.requires_audit);

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT pg_temp.exitpass_assisted_intake_uuid('assisted-intake:role-permission:SITE_OPERATOR:' || permission.permission_code),
       role.role_id, permission.permission_id, 'ACTIVE',
       'OPERATOR_CONSOLE_ASSISTED_INTAKE',
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
       '2020-01-01T00:00:00Z',
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles role
JOIN identity.permissions permission
  ON permission.permission_code IN (SELECT permission_code FROM assisted_intake_permissions)
WHERE role.role_code = 'SITE_OPERATOR'
  AND role.role_status = 'ACTIVE'
  AND role.role_provenance = 'CANONICAL_ROLE'
  AND role.human_assignable
  AND NOT EXISTS (
      SELECT 1 FROM identity.role_permissions active
      WHERE active.role_id = role.role_id
        AND active.permission_id = permission.permission_id
        AND active.binding_status = 'ACTIVE')
ON CONFLICT (role_permission_id) DO UPDATE SET
    binding_status = 'ACTIVE', effective_to = NULL, revoked_at = NULL,
    revocation_reason_code = NULL,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now(), row_version = identity.role_permissions.row_version + 1
WHERE identity.role_permissions.binding_status <> 'ACTIVE'
   OR identity.role_permissions.effective_to IS NOT NULL
   OR identity.role_permissions.revoked_at IS NOT NULL
   OR identity.role_permissions.revocation_reason_code IS NOT NULL;

COMMIT;
