-- HikCentral vendor session projection schema patch for existing ExitPass v1.2 databases.
--
-- Source of truth:
--   ExitPass_Full_Database_Creation_DDL_v1.2.sql
--
-- Purpose:
--   Apply only the #280/#282 HikCentral projection schema objects that may be
--   missing from an existing dev/UAT database.
--   This is the prerequisite patch referenced by
--   docs/hikcentral-projection-live-uat.md before sync-target seeding.
--
-- Safety:
--   - No seed data.
--   - No secrets.
--   - No DROP statements.
--   - No full database rebuild.
--   - Idempotent table, column, constraint, comment, and index creation.
--
-- Required pre-existing baseline objects:
--   - schema sessions
--   - schema sites and tables sites.sites, sites.site_groups
--   - schema integration and table integration.vendor_systems
--   - schema identity and table identity.service_identities
--
-- Authority boundary:
--   Projection rows are continuity snapshots/read models only. They are not
--   parking-session truth, tariff truth, payment truth, payment finality, or
--   exit authority.

BEGIN;

-- ------------------------------------------------------------
-- sessions.vendor_session_projections
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sessions.vendor_session_projections (
    vendor_session_projection_id uuid DEFAULT gen_random_uuid() NOT NULL,
    vendor_system_id uuid,
    site_id uuid,
    site_group_id uuid,
    parking_lot_index_code text,
    parking_lot_name text,
    passageway_index_code text,
    passageway_name text,
    lane_index_code text,
    lane_name text,
    lane_direction text,
    vendor_record_guid text,
    card_num text,
    plate_license text,
    enter_time timestamptz,
    exit_time timestamptz,
    allow_type text,
    allow_result text,
    image_url text,
    source_api text NOT NULL,
    source_payload_hash char(64) NOT NULL,
    source_payload_reference text,
    source_event_at timestamptz,
    stable_identity_type text NOT NULL,
    stable_identity_key text NOT NULL,
    first_seen_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    last_refreshed_at timestamptz NOT NULL,
    projection_status text NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_vendor_session_projections PRIMARY KEY (vendor_session_projection_id)
);

ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS vendor_session_projection_id uuid DEFAULT gen_random_uuid() NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS vendor_system_id uuid;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS site_id uuid;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS site_group_id uuid;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS parking_lot_index_code text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS parking_lot_name text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS passageway_index_code text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS passageway_name text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS lane_index_code text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS lane_name text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS lane_direction text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS vendor_record_guid text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS card_num text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS plate_license text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS enter_time timestamptz;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS exit_time timestamptz;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS allow_type text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS allow_result text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS image_url text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS source_api text NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS source_payload_hash char(64) NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS source_payload_reference text;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS source_event_at timestamptz;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS stable_identity_type text NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS stable_identity_key text NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS first_seen_at timestamptz NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS last_seen_at timestamptz NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS last_refreshed_at timestamptz NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS projection_status text NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS correlation_id uuid;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS created_at timestamptz DEFAULT now() NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS created_by_service_identity_id uuid NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS updated_at timestamptz DEFAULT now() NOT NULL;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS updated_by_service_identity_id uuid;
ALTER TABLE sessions.vendor_session_projections ADD COLUMN IF NOT EXISTS row_version bigint DEFAULT 1 NOT NULL;

COMMENT ON TABLE sessions.vendor_session_projections IS 'ExitPass-owned read model of latest-known vendor session continuity snapshots. This projection is not parking-session authority, tariff authority, payment finality, or exit authorization.';
COMMENT ON COLUMN sessions.vendor_session_projections.vendor_session_projection_id IS 'Canonical identifier of the projection snapshot.';
COMMENT ON COLUMN sessions.vendor_session_projections.vendor_system_id IS 'Vendor PMS that supplied the passageway source record, where mapped.';
COMMENT ON COLUMN sessions.vendor_session_projections.site_id IS 'ExitPass site scope, where mapped.';
COMMENT ON COLUMN sessions.vendor_session_projections.site_group_id IS 'ExitPass site group scope, where mapped.';
COMMENT ON COLUMN sessions.vendor_session_projections.parking_lot_index_code IS 'HikCentral parking lot index code.';
COMMENT ON COLUMN sessions.vendor_session_projections.parking_lot_name IS 'HikCentral parking lot display name.';
COMMENT ON COLUMN sessions.vendor_session_projections.passageway_index_code IS 'HikCentral passageway index code.';
COMMENT ON COLUMN sessions.vendor_session_projections.passageway_name IS 'HikCentral passageway display name.';
COMMENT ON COLUMN sessions.vendor_session_projections.lane_index_code IS 'HikCentral lane index code.';
COMMENT ON COLUMN sessions.vendor_session_projections.lane_name IS 'HikCentral lane display name.';
COMMENT ON COLUMN sessions.vendor_session_projections.lane_direction IS 'HikCentral lane direction, where supplied.';
COMMENT ON COLUMN sessions.vendor_session_projections.vendor_record_guid IS 'HikCentral passageway record GUID, where supplied.';
COMMENT ON COLUMN sessions.vendor_session_projections.card_num IS 'HikCentral personInfo.cardNum value used as ticket/card lookup value.';
COMMENT ON COLUMN sessions.vendor_session_projections.plate_license IS 'Optional HikCentral plate license value.';
COMMENT ON COLUMN sessions.vendor_session_projections.enter_time IS 'Entry timestamp from the vendor passageway record.';
COMMENT ON COLUMN sessions.vendor_session_projections.exit_time IS 'Exit timestamp from the vendor passageway record.';
COMMENT ON COLUMN sessions.vendor_session_projections.allow_type IS 'Vendor allow type from the passageway record.';
COMMENT ON COLUMN sessions.vendor_session_projections.allow_result IS 'Vendor allow result from the passageway record.';
COMMENT ON COLUMN sessions.vendor_session_projections.image_url IS 'Vendor image URL reference, where supplied.';
COMMENT ON COLUMN sessions.vendor_session_projections.source_api IS 'Vendor source API path used to build the projection.';
COMMENT ON COLUMN sessions.vendor_session_projections.source_payload_hash IS 'SHA-256 hash of the normalized source payload; raw payload is not retained in this table.';
COMMENT ON COLUMN sessions.vendor_session_projections.source_payload_reference IS 'Safe source payload reference such as vendor record GUID or derived reference.';
COMMENT ON COLUMN sessions.vendor_session_projections.source_event_at IS 'Best available source event timestamp from enter/exit time.';
COMMENT ON COLUMN sessions.vendor_session_projections.stable_identity_type IS 'Stable identity strategy used for idempotent upsert.';
COMMENT ON COLUMN sessions.vendor_session_projections.stable_identity_key IS 'Stable projection identity key used for idempotent upsert.';
COMMENT ON COLUMN sessions.vendor_session_projections.first_seen_at IS 'First time ExitPass observed this projection identity.';
COMMENT ON COLUMN sessions.vendor_session_projections.last_seen_at IS 'Most recent time ExitPass observed this projection identity.';
COMMENT ON COLUMN sessions.vendor_session_projections.last_refreshed_at IS 'Most recent time ExitPass refreshed this projection snapshot.';
COMMENT ON COLUMN sessions.vendor_session_projections.projection_status IS 'Projection snapshot status: ACTIVE, EXITED, STALE, INVALIDATED, or UNKNOWN.';
COMMENT ON COLUMN sessions.vendor_session_projections.correlation_id IS 'Cross-service correlation identifier for the pull that last refreshed this projection.';
COMMENT ON COLUMN sessions.vendor_session_projections.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sessions.vendor_session_projections.created_by_service_identity_id IS 'Service identity that created the projection.';
COMMENT ON COLUMN sessions.vendor_session_projections.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN sessions.vendor_session_projections.updated_by_service_identity_id IS 'Service identity that last updated the projection.';
COMMENT ON COLUMN sessions.vendor_session_projections.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- sessions.vendor_session_projection_sync_targets
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sessions.vendor_session_projection_sync_targets (
    projection_sync_target_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_id uuid NOT NULL,
    site_group_id uuid NOT NULL,
    vendor_system_id uuid NOT NULL,
    parking_lot_index_code text NOT NULL,
    parking_lot_name text NULL,
    enabled_flag boolean DEFAULT false NOT NULL,
    poll_interval_seconds integer DEFAULT 60 NOT NULL,
    lookback_window_minutes integer DEFAULT 180 NOT NULL,
    page_size integer DEFAULT 100 NOT NULL,
    last_success_at timestamptz NULL,
    last_failure_at timestamptz NULL,
    last_attempt_at timestamptz NULL,
    health_status text DEFAULT 'DISABLED' NOT NULL,
    failure_count integer DEFAULT 0 NOT NULL,
    last_error_code text NULL,
    last_error_message text NULL,
    last_lock_contention_at timestamptz NULL,
    lock_contention_count integer DEFAULT 0 NOT NULL,
    correlation_id uuid NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    row_version integer DEFAULT 1 NOT NULL,
    CONSTRAINT pk_vendor_session_projection_sync_targets PRIMARY KEY (projection_sync_target_id)
);

ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS projection_sync_target_id uuid DEFAULT gen_random_uuid() NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS site_id uuid NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS site_group_id uuid NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS vendor_system_id uuid NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS parking_lot_index_code text NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS parking_lot_name text NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS enabled_flag boolean DEFAULT false NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS poll_interval_seconds integer DEFAULT 60 NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS lookback_window_minutes integer DEFAULT 180 NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS page_size integer DEFAULT 100 NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS last_success_at timestamptz NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS last_failure_at timestamptz NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS health_status text DEFAULT 'DISABLED' NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ALTER COLUMN health_status SET DEFAULT 'DISABLED';
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS failure_count integer DEFAULT 0 NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS last_error_code text NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS last_error_message text NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS last_lock_contention_at timestamptz NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS lock_contention_count integer DEFAULT 0 NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS correlation_id uuid NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS created_at timestamptz DEFAULT now() NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS updated_at timestamptz DEFAULT now() NOT NULL;
ALTER TABLE sessions.vendor_session_projection_sync_targets ADD COLUMN IF NOT EXISTS row_version integer DEFAULT 1 NOT NULL;

COMMENT ON TABLE sessions.vendor_session_projection_sync_targets IS 'Site-scoped HikCentral vendor session projection scheduler targets. This table configures refresh of continuity snapshots and is not parking-session authority, tariff authority, payment finality, or exit authorization.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.projection_sync_target_id IS 'Canonical identifier of the projection sync target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.site_id IS 'ExitPass site scope for this projection sync target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.site_group_id IS 'ExitPass site group scope for this projection sync target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.vendor_system_id IS 'Vendor PMS that supplies passageway records for this target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.parking_lot_index_code IS 'HikCentral parking lot index code scoped to this target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.parking_lot_name IS 'Optional HikCentral parking lot display name.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.enabled_flag IS 'Whether the centralized scheduler may run this target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.poll_interval_seconds IS 'Minimum interval between scheduled attempts for this target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.lookback_window_minutes IS 'Lookback window used for passageway record pulls.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.page_size IS 'Vendor API page size used for passageway record pulls.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.last_success_at IS 'Last successful projection sync completion timestamp.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.last_failure_at IS 'Last failed projection sync completion timestamp.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.last_attempt_at IS 'Last projection sync attempt timestamp.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.health_status IS 'Operational target health: HEALTHY, DEGRADED, FAILING, DISABLED, or UNKNOWN.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.failure_count IS 'Consecutive failure count for this target.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.last_error_code IS 'Last sync error code, when failed.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.last_error_message IS 'Last sync error message, when failed.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.last_lock_contention_at IS 'Last cycle deferred because another scheduler held the target-scoped advisory lock.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.lock_contention_count IS 'Cumulative target-scoped advisory lock contention count.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.correlation_id IS 'Correlation identifier for the last scheduler/manual attempt.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- Primary keys, foreign keys, unique constraints, and checks
-- ------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'pk_vendor_session_projections'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT pk_vendor_session_projections PRIMARY KEY (vendor_session_projection_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'pk_vendor_session_projection_sync_targets'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT pk_vendor_session_projection_sync_targets PRIMARY KEY (projection_sync_target_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projections__vendor_system_id'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT fk_vendor_session_projections__vendor_system_id
            FOREIGN KEY (vendor_system_id)
            REFERENCES integration.vendor_systems(vendor_system_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projections__site_id'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT fk_vendor_session_projections__site_id
            FOREIGN KEY (site_id)
            REFERENCES sites.sites(site_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projections__site_group_id'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT fk_vendor_session_projections__site_group_id
            FOREIGN KEY (site_group_id)
            REFERENCES sites.site_groups(site_group_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projections__created_by_service_identity_id'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT fk_vendor_session_projections__created_by_service_identity_id
            FOREIGN KEY (created_by_service_identity_id)
            REFERENCES identity.service_identities(service_identity_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projections__updated_by_service_identity_id'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT fk_vendor_session_projections__updated_by_service_identity_id
            FOREIGN KEY (updated_by_service_identity_id)
            REFERENCES identity.service_identities(service_identity_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projection_sync_targets__site_id'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT fk_vendor_session_projection_sync_targets__site_id
            FOREIGN KEY (site_id)
            REFERENCES sites.sites(site_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projection_sync_targets__site_group_id'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT fk_vendor_session_projection_sync_targets__site_group_id
            FOREIGN KEY (site_group_id)
            REFERENCES sites.site_groups(site_group_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_vendor_session_projection_sync_targets__vendor_system_id'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT fk_vendor_session_projection_sync_targets__vendor_system_id
            FOREIGN KEY (vendor_system_id)
            REFERENCES integration.vendor_systems(vendor_system_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projections__row_version_positive'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT ck_vendor_session_projections__row_version_positive
            CHECK (row_version > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projections__projection_status'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT ck_vendor_session_projections__projection_status
            CHECK (projection_status IN ('ACTIVE', 'EXITED', 'STALE', 'INVALIDATED', 'UNKNOWN'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projections__source_payload_hash_sha256'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT ck_vendor_session_projections__source_payload_hash_sha256
            CHECK (source_payload_hash ~ '^[0-9a-f]{64}$');
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projections__stable_identity_required'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT ck_vendor_session_projections__stable_identity_required
            CHECK (length(btrim(stable_identity_type)) > 0 AND length(btrim(stable_identity_key)) > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projections__seen_window'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT ck_vendor_session_projections__seen_window
            CHECK (last_seen_at >= first_seen_at AND last_refreshed_at >= first_seen_at);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projection_sync_targets__row_version_positive'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_session_projection_sync_targets__row_version_positive
            CHECK (row_version > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projection_sync_targets__health_status'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_session_projection_sync_targets__health_status
            CHECK (health_status IN ('HEALTHY', 'DEGRADED', 'FAILING', 'DISABLED', 'DEFERRED', 'UNKNOWN'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projection_sync_targets__parking_lot_required'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_session_projection_sync_targets__parking_lot_required
            CHECK (length(btrim(parking_lot_index_code)) > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projection_sync_targets__poll_interval_positive'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_session_projection_sync_targets__poll_interval_positive
            CHECK (poll_interval_seconds > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projection_sync_targets__lookback_positive'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_session_projection_sync_targets__lookback_positive
            CHECK (lookback_window_minutes > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projection_sync_targets__page_size_bounds'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_session_projection_sync_targets__page_size_bounds
            CHECK (page_size BETWEEN 1 AND 500);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_session_projection_sync_targets__failure_count_non_negative'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_session_projection_sync_targets__failure_count_non_negative
            CHECK (failure_count >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_vendor_projection_targets__lock_contention_non_negative'
          AND conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_projection_targets__lock_contention_non_negative
            CHECK (lock_contention_count >= 0);
    END IF;
END $$;

-- Unique constraint/indexes from the full DDL. Idempotency and upstream GUID
-- uniqueness are isolated by the complete persisted target scope.
ALTER TABLE sessions.vendor_session_projections
    DROP CONSTRAINT IF EXISTS uq_vendor_session_projections__stable_identity_key;

DROP INDEX IF EXISTS sessions.uq_vendor_session_projections__stable_identity_key;

CREATE UNIQUE INDEX IF NOT EXISTS uq_vendor_session_projections__target_stable_identity
ON sessions.vendor_session_projections (
    vendor_system_id,
    site_group_id,
    site_id,
    parking_lot_index_code,
    stable_identity_key
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'uq_vendor_session_projections__target_stable_identity'
          AND conrelid = 'sessions.vendor_session_projections'::regclass
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT uq_vendor_session_projections__target_stable_identity
            UNIQUE USING INDEX uq_vendor_session_projections__target_stable_identity;
    END IF;
END $$;

DROP INDEX IF EXISTS sessions.ux_vendor_session_projections__vendor_record_guid;

CREATE UNIQUE INDEX IF NOT EXISTS ux_vendor_session_projections__target_vendor_record_guid
ON sessions.vendor_session_projections (
    vendor_system_id,
    site_group_id,
    site_id,
    parking_lot_index_code,
    vendor_record_guid
)
WHERE vendor_system_id IS NOT NULL
  AND site_group_id IS NOT NULL
  AND site_id IS NOT NULL
  AND parking_lot_index_code IS NOT NULL
  AND vendor_record_guid IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_vendor_session_projection_sync_targets__scope
ON sessions.vendor_session_projection_sync_targets (site_id, vendor_system_id, parking_lot_index_code);

-- Normal indexes from the full DDL.
CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__card_num
ON sessions.vendor_session_projections (card_num)
WHERE card_num IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__plate_license
ON sessions.vendor_session_projections (plate_license)
WHERE plate_license IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__parking_lot_card
ON sessions.vendor_session_projections (parking_lot_index_code, card_num)
WHERE card_num IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__site_card
ON sessions.vendor_session_projections (site_id, card_num)
WHERE site_id IS NOT NULL AND card_num IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__status_refreshed
ON sessions.vendor_session_projections (projection_status, last_refreshed_at DESC);

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__active_open
ON sessions.vendor_session_projections (parking_lot_index_code, last_refreshed_at DESC)
WHERE projection_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__last_refreshed_at
ON sessions.vendor_session_projections (last_refreshed_at);

CREATE INDEX IF NOT EXISTS ix_vendor_session_projections__correlation_id
ON sessions.vendor_session_projections (correlation_id)
WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_vendor_session_projection_sync_targets__enabled_due
ON sessions.vendor_session_projection_sync_targets (enabled_flag, last_attempt_at, poll_interval_seconds)
WHERE enabled_flag = TRUE;

CREATE INDEX IF NOT EXISTS ix_vendor_session_projection_sync_targets__site
ON sessions.vendor_session_projection_sync_targets (site_id);

CREATE INDEX IF NOT EXISTS ix_vendor_session_projection_sync_targets__parking_lot
ON sessions.vendor_session_projection_sync_targets (parking_lot_index_code);

CREATE INDEX IF NOT EXISTS ix_vendor_session_projection_sync_targets__vendor_system
ON sessions.vendor_session_projection_sync_targets (vendor_system_id);

CREATE INDEX IF NOT EXISTS ix_vendor_session_projection_sync_targets__health
ON sessions.vendor_session_projection_sync_targets (health_status, last_success_at DESC, last_failure_at DESC);

CREATE INDEX IF NOT EXISTS ix_vendor_session_projection_sync_targets__correlation_id
ON sessions.vendor_session_projection_sync_targets (correlation_id)
WHERE correlation_id IS NOT NULL;

COMMIT;

-- ------------------------------------------------------------
-- Verification queries
-- ------------------------------------------------------------
-- 1. Confirm both tables exist.
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'sessions'
  AND table_name IN (
      'vendor_session_projections',
      'vendor_session_projection_sync_targets'
  )
ORDER BY table_name;

-- Expected: 2 rows.

-- 2. Confirm expected constraints exist.
SELECT conrelid::regclass AS table_name, conname, contype
FROM pg_constraint
WHERE conrelid IN (
    'sessions.vendor_session_projections'::regclass,
    'sessions.vendor_session_projection_sync_targets'::regclass
)
  AND conname IN (
      'pk_vendor_session_projections',
      'pk_vendor_session_projection_sync_targets',
      'fk_vendor_session_projections__vendor_system_id',
      'fk_vendor_session_projections__site_id',
      'fk_vendor_session_projections__site_group_id',
      'fk_vendor_session_projections__created_by_service_identity_id',
      'fk_vendor_session_projections__updated_by_service_identity_id',
      'fk_vendor_session_projection_sync_targets__site_id',
      'fk_vendor_session_projection_sync_targets__site_group_id',
      'fk_vendor_session_projection_sync_targets__vendor_system_id',
      'ck_vendor_session_projections__row_version_positive',
      'ck_vendor_session_projections__projection_status',
      'ck_vendor_session_projections__source_payload_hash_sha256',
      'ck_vendor_session_projections__stable_identity_required',
      'ck_vendor_session_projections__seen_window',
      'ck_vendor_session_projection_sync_targets__row_version_positive',
      'ck_vendor_session_projection_sync_targets__health_status',
      'ck_vendor_session_projection_sync_targets__parking_lot_required',
      'ck_vendor_session_projection_sync_targets__poll_interval_positive',
      'ck_vendor_session_projection_sync_targets__lookback_positive',
      'ck_vendor_session_projection_sync_targets__page_size_bounds',
      'ck_vendor_session_projection_sync_targets__failure_count_non_negative',
      'ck_vendor_projection_targets__lock_contention_non_negative',
      'uq_vendor_session_projections__target_stable_identity'
  )
ORDER BY table_name, contype, conname;

-- Expected: 24 rows.

-- 3. Confirm expected indexes exist.
SELECT schemaname, tablename, indexname
FROM pg_indexes
WHERE schemaname = 'sessions'
  AND indexname IN (
      'ux_vendor_session_projections__target_vendor_record_guid',
      'ux_vendor_session_projection_sync_targets__scope',
      'ix_vendor_session_projections__card_num',
      'ix_vendor_session_projections__plate_license',
      'ix_vendor_session_projections__parking_lot_card',
      'ix_vendor_session_projections__site_card',
      'ix_vendor_session_projections__status_refreshed',
      'ix_vendor_session_projections__active_open',
      'ix_vendor_session_projections__last_refreshed_at',
      'ix_vendor_session_projections__correlation_id',
      'ix_vendor_session_projection_sync_targets__enabled_due',
      'ix_vendor_session_projection_sync_targets__site',
      'ix_vendor_session_projection_sync_targets__parking_lot',
      'ix_vendor_session_projection_sync_targets__vendor_system',
      'ix_vendor_session_projection_sync_targets__health',
      'ix_vendor_session_projection_sync_targets__correlation_id'
  )
ORDER BY tablename, indexname;

-- Expected: 16 rows.

-- 4. Confirm authority-boundary comments are present.
SELECT c.relname AS table_name, obj_description(c.oid, 'pg_class') AS table_comment
FROM pg_class c
JOIN pg_namespace n
  ON n.oid = c.relnamespace
WHERE n.nspname = 'sessions'
  AND c.relname IN (
      'vendor_session_projections',
      'vendor_session_projection_sync_targets'
  )
ORDER BY c.relname;

-- Expected comments mention that projection/scheduler data is not parking-session
-- authority, tariff authority, payment finality, or exit authorization.
