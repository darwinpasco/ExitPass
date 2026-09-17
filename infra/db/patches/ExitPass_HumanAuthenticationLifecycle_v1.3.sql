BEGIN;

ALTER TYPE identity.human_session_audience_enum
    ADD VALUE IF NOT EXISTS 'NATIVE_PARKING_APP';

ALTER TABLE identity.local_credentials
    ADD COLUMN IF NOT EXISTS temporary_password_expires_at timestamptz;

UPDATE identity.local_credentials
SET temporary_password_expires_at = created_at + interval '72 hours'
WHERE credential_status = 'CHANGE_REQUIRED'
  AND temporary_password_expires_at IS NULL;

UPDATE identity.local_credentials
SET temporary_password_expires_at = NULL
WHERE credential_status <> 'CHANGE_REQUIRED'
  AND temporary_password_expires_at IS NOT NULL;

ALTER TABLE identity.local_credentials
    DROP CONSTRAINT IF EXISTS ck_local_credentials_temporary_password_expiry;

ALTER TABLE identity.local_credentials
    ADD CONSTRAINT ck_local_credentials_temporary_password_expiry CHECK (
        (credential_status = 'CHANGE_REQUIRED' AND temporary_password_expires_at IS NOT NULL)
        OR (credential_status <> 'CHANGE_REQUIRED' AND temporary_password_expires_at IS NULL)
    );

CREATE INDEX IF NOT EXISTS ix_local_credentials_temporary_password_expiry
    ON identity.local_credentials (temporary_password_expires_at)
    WHERE credential_status = 'CHANGE_REQUIRED';

COMMIT;
