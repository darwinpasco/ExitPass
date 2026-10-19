-- Enforces one live Device Administration identity per physical APT terminal.
BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT lower(btrim(terminal_identifier))
        FROM identity.devices
        WHERE device_type = 'ASSISTED_PAYMENT_TERMINAL'
          AND lifecycle_status NOT IN ('REPLACED', 'REVOKED')
        GROUP BY lower(btrim(terminal_identifier))
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Active APT terminal identifiers are not unique';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.devices
        WHERE device_type = 'ASSISTED_PAYMENT_TERMINAL'
          AND (terminal_identifier IS NULL OR btrim(terminal_identifier) = '')
    ) THEN
        RAISE EXCEPTION 'APT terminal identifiers must be nonblank';
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'identity.devices'::regclass
          AND conname = 'ck_devices__apt_terminal_identifier_nonblank'
    ) THEN
        ALTER TABLE identity.devices
            ADD CONSTRAINT ck_devices__apt_terminal_identifier_nonblank
            CHECK (
                device_type <> 'ASSISTED_PAYMENT_TERMINAL'
                OR (terminal_identifier IS NOT NULL AND btrim(terminal_identifier) <> '')
            );
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_devices__live_apt_terminal_identifier
    ON identity.devices (lower(btrim(terminal_identifier)))
    WHERE device_type = 'ASSISTED_PAYMENT_TERMINAL'
      AND lifecycle_status NOT IN ('REPLACED', 'REVOKED');

COMMIT;
