\set ON_ERROR_STOP on

BEGIN;

CREATE OR REPLACE FUNCTION pg_temp.exitpass_annex_e1_permission_uuid(input text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
STRICT
AS $$
    SELECT (
        substr(md5(input), 1, 8) || '-' ||
        substr(md5(input), 9, 4) || '-' ||
        substr(md5(input), 13, 4) || '-' ||
        substr(md5(input), 17, 4) || '-' ||
        substr(md5(input), 21, 12)
    )::uuid
$$;

DO $$ BEGIN
    IF (SELECT count(*) FROM identity.service_identities
        WHERE service_identity_code = 'seed.reference-data'
          AND identity_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Canonical reference-data service identity is unavailable or ambiguous.';
    END IF;
END $$;

INSERT INTO identity.permissions (
    permission_id, permission_code, permission_name, permission_description,
    permission_domain, permission_action, permission_status, is_sensitive,
    requires_audit, created_by_service_identity_id, updated_by_service_identity_id)
SELECT pg_temp.exitpass_annex_e1_permission_uuid('exitpass:v1.3:rbac:permission:' || permission.permission_code),
       permission.permission_code, permission.permission_name, permission.permission_description,
       'fiscal-reporting', permission.permission_action, 'ACTIVE', true,
       true, service.service_identity_id, service.service_identity_id
FROM (VALUES
    ('fiscal-reporting.annex-e1.known-zero.attest', 'Attest Annex E-1 known-zero facts',
     'Submit an explicit, approved known-zero Annex E-1 accounting fact for an authorized Site.', 'attest'),
    ('fiscal-reporting.annex-e1.correct', 'Correct Annex E-1 workbooks',
     'Generate an immutable Annex E-1 correction that supersedes an existing workbook under explicit approval.', 'correct')
) AS permission(permission_code, permission_name, permission_description, permission_action)
CROSS JOIN identity.service_identities service
WHERE service.service_identity_code = 'seed.reference-data'
  AND service.identity_status::text = 'ACTIVE'
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    permission_description = EXCLUDED.permission_description,
    permission_domain = EXCLUDED.permission_domain,
    permission_action = EXCLUDED.permission_action,
    permission_status = 'ACTIVE',
    is_sensitive = true,
    requires_audit = true,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = identity.permissions.row_version + 1
WHERE (identity.permissions.permission_name, identity.permissions.permission_description,
       identity.permissions.permission_domain, identity.permissions.permission_action,
       identity.permissions.permission_status::text, identity.permissions.is_sensitive,
       identity.permissions.requires_audit)
      IS DISTINCT FROM
      (EXCLUDED.permission_name, EXCLUDED.permission_description,
       EXCLUDED.permission_domain, EXCLUDED.permission_action,
       EXCLUDED.permission_status::text, EXCLUDED.is_sensitive,
       EXCLUDED.requires_audit);

DO $$ BEGIN
    IF (SELECT count(*) FROM identity.permissions
        WHERE permission_code IN (
            'fiscal-reporting.annex-e1.known-zero.attest',
            'fiscal-reporting.annex-e1.correct')
          AND permission_status::text = 'ACTIVE'
          AND is_sensitive
          AND requires_audit) <> 2 THEN
        RAISE EXCEPTION 'The governed Annex E-1 permission catalog is incomplete.';
    END IF;
END $$;

COMMIT;

