/*
 * ExitPass v1.3 additive Operator Console session-context correction.
 *
 * The locked v1.2 device, assignment, shift, identity, and H-006 session tables
 * remain unchanged. This table is the durable server-owned link between one
 * live H-006 Operator Console session and its resolved canonical operating context.
 * It stores no browser credential or raw session token.
 */

CREATE TABLE IF NOT EXISTS operator_console.operator_session_contexts (
    operator_session_context_id uuid DEFAULT gen_random_uuid() NOT NULL,
    human_session_id uuid NOT NULL,
    operator_user_id uuid NOT NULL,
    operator_device_binding_id uuid NOT NULL,
    operator_shift_id uuid NOT NULL,
    site_group_id uuid NOT NULL,
    site_id uuid NOT NULL,
    authorization_epoch_snapshot bigint NOT NULL,
    credential_version_snapshot bigint NOT NULL,
    context_status varchar(32) NOT NULL,
    bound_at timestamptz NOT NULL,
    last_validated_at timestamptz NOT NULL,
    invalidated_at timestamptz,
    invalidation_reason_code varchar(96),
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_operator_session_contexts PRIMARY KEY (operator_session_context_id),
    CONSTRAINT ux_operator_session_contexts__human_session UNIQUE (human_session_id),
    CONSTRAINT fk_operator_session_contexts__human_session FOREIGN KEY (human_session_id)
        REFERENCES identity.human_sessions(human_session_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_session_contexts__operator_user FOREIGN KEY (operator_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_session_contexts__device_binding FOREIGN KEY (operator_device_binding_id)
        REFERENCES operator_console.operator_device_bindings(operator_device_binding_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_session_contexts__operator_shift FOREIGN KEY (operator_shift_id)
        REFERENCES operator_console.operator_shifts(operator_shift_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_session_contexts__site_group FOREIGN KEY (site_group_id)
        REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_session_contexts__site FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_operator_session_contexts__epochs_nonnegative CHECK (
        authorization_epoch_snapshot >= 0 AND credential_version_snapshot >= 0
    ),
    CONSTRAINT ck_operator_session_contexts__status CHECK (
        context_status IN ('ACTIVE', 'INVALIDATED')
    ),
    CONSTRAINT ck_operator_session_contexts__invalidation CHECK (
        (context_status = 'ACTIVE' AND invalidated_at IS NULL AND invalidation_reason_code IS NULL)
        OR (context_status = 'INVALIDATED' AND invalidated_at IS NOT NULL AND invalidation_reason_code IS NOT NULL)
    ),
    CONSTRAINT ck_operator_session_contexts__validation_time CHECK (last_validated_at >= bound_at),
    CONSTRAINT ck_operator_session_contexts__row_version_positive CHECK (row_version > 0)
);

COMMENT ON TABLE operator_console.operator_session_contexts IS
    'Server-owned H-006 Operator Console device, active-shift, and effective Site/Site Group binding; contains no browser proof or session secret.';

CREATE INDEX IF NOT EXISTS ix_operator_session_contexts__active_device
    ON operator_console.operator_session_contexts (operator_device_binding_id, last_validated_at DESC)
    WHERE context_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_operator_session_contexts__active_shift
    ON operator_console.operator_session_contexts (operator_shift_id, last_validated_at DESC)
    WHERE context_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_operator_session_contexts__active_user
    ON operator_console.operator_session_contexts (operator_user_id, last_validated_at DESC)
    WHERE context_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_operator_session_contexts__correlation
    ON operator_console.operator_session_contexts (correlation_id);
