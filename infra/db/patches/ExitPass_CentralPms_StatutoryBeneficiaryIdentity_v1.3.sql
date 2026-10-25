CREATE TABLE IF NOT EXISTS discounts.statutory_beneficiary_identities (
    statutory_beneficiary_identity_id uuid PRIMARY KEY,
    statutory_discount_decision_command_id uuid NOT NULL UNIQUE
        REFERENCES discounts.statutory_discount_decision_commands(statutory_discount_decision_command_id),
    site_id uuid NOT NULL REFERENCES sites.sites(site_id),
    entitlement_type varchar(64) NOT NULL,
    statutory_id_type varchar(64) NOT NULL,
    protected_identity_envelope bytea NOT NULL,
    protection_key_reference varchar(128) NOT NULL,
    protection_key_version varchar(64) NOT NULL,
    envelope_format_version smallint NOT NULL,
    captured_by_actor_ref uuid NOT NULL REFERENCES identity.users(user_id),
    captured_by_channel varchar(32) NOT NULL,
    captured_at timestamptz NOT NULL,
    last_updated_by_actor_ref uuid NOT NULL REFERENCES identity.users(user_id),
    last_updated_by_channel varchar(32) NOT NULL,
    last_updated_at timestamptz NOT NULL,
    reviewed_by_actor_ref uuid NULL REFERENCES identity.users(user_id),
    reviewed_at timestamptz NULL,
    approval_decision_reference varchar(200) NULL,
    correlation_reference uuid NOT NULL,
    is_final boolean NOT NULL DEFAULT false,
    CONSTRAINT ck_statutory_beneficiary_identity_capture_channel
        CHECK (captured_by_channel IN ('OPERATOR_CONSOLE', 'MANAGEMENT_PLATFORM')),
    CONSTRAINT ck_statutory_beneficiary_identity_update_channel
        CHECK (last_updated_by_channel IN ('OPERATOR_CONSOLE', 'MANAGEMENT_PLATFORM')),
    CONSTRAINT ck_statutory_beneficiary_identity_envelope
        CHECK (octet_length(protected_identity_envelope) >= 31 AND envelope_format_version > 0),
    CONSTRAINT ck_statutory_beneficiary_identity_final
        CHECK (NOT is_final OR (reviewed_by_actor_ref IS NOT NULL AND reviewed_at IS NOT NULL AND approval_decision_reference IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_statutory_beneficiary_identities__site
    ON discounts.statutory_beneficiary_identities(site_id);

CREATE OR REPLACE FUNCTION discounts.reject_final_statutory_beneficiary_identity_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.is_final THEN
        RAISE EXCEPTION 'final statutory beneficiary identity is immutable'
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_statutory_beneficiary_identity_final_immutable
    ON discounts.statutory_beneficiary_identities;
CREATE TRIGGER trg_statutory_beneficiary_identity_final_immutable
BEFORE UPDATE OR DELETE ON discounts.statutory_beneficiary_identities
FOR EACH ROW EXECUTE FUNCTION discounts.reject_final_statutory_beneficiary_identity_mutation();

COMMENT ON TABLE discounts.statutory_beneficiary_identities IS
    'Purpose-specific encrypted beneficiary identity captured only by Operator Console or Management Platform for final statutory fiscal snapshotting.';
