BEGIN;

ALTER TABLE sessions.vendor_session_projections
    ADD COLUMN IF NOT EXISTS source_adapter_identity_id uuid;
ALTER TABLE core.parking_sessions
    ADD COLUMN IF NOT EXISTS source_adapter_identity_id uuid;
ALTER TABLE core.tariff_snapshots
    ADD COLUMN IF NOT EXISTS source_adapter_identity_id uuid;

DO $do$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_vendor_session_projections__source_adapter_identity_id') THEN
        ALTER TABLE sessions.vendor_session_projections ADD CONSTRAINT fk_vendor_session_projections__source_adapter_identity_id
            FOREIGN KEY (source_adapter_identity_id) REFERENCES identity.service_identities(service_identity_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_parking_sessions__source_adapter_identity_id') THEN
        ALTER TABLE core.parking_sessions ADD CONSTRAINT fk_parking_sessions__source_adapter_identity_id
            FOREIGN KEY (source_adapter_identity_id) REFERENCES identity.service_identities(service_identity_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tariff_snapshots__source_adapter_identity_id') THEN
        ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__source_adapter_identity_id
            FOREIGN KEY (source_adapter_identity_id) REFERENCES identity.service_identities(service_identity_id);
    END IF;
END
$do$;

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__source_adapter_identity_id
    ON sessions.vendor_session_projections (source_adapter_identity_id);
CREATE INDEX IF NOT EXISTS ix_parking_sessions__source_adapter_identity_id
    ON core.parking_sessions (source_adapter_identity_id);
CREATE INDEX IF NOT EXISTS ix_tariff_snapshots__source_adapter_identity_id
    ON core.tariff_snapshots (source_adapter_identity_id);

COMMENT ON COLUMN sessions.vendor_session_projections.source_adapter_identity_id IS
    'Site Integration Adapter service identity that supplied the provider-neutral projection.';
COMMENT ON COLUMN core.parking_sessions.source_adapter_identity_id IS
    'Immutable Site Integration Adapter service identity used for the resolved vendor session.';
COMMENT ON COLUMN core.tariff_snapshots.source_adapter_identity_id IS
    'Immutable Site Integration Adapter service identity used for authoritative vendor tariff evidence.';

COMMIT;
