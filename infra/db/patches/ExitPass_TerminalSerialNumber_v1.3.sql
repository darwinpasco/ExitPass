BEGIN;

ALTER TABLE identity.devices
    ADD COLUMN IF NOT EXISTS terminal_serial_number varchar(128);

ALTER TABLE identity.devices
    DROP CONSTRAINT IF EXISTS ck_devices__apt_terminal_serial_number_nonblank;

ALTER TABLE identity.devices
    ADD CONSTRAINT ck_devices__apt_terminal_serial_number_nonblank CHECK (
        device_type <> 'ASSISTED_PAYMENT_TERMINAL'
        OR terminal_serial_number IS NULL
        OR btrim(terminal_serial_number) <> ''
    );

CREATE UNIQUE INDEX IF NOT EXISTS uq_devices__live_apt_terminal_serial_number
    ON identity.devices (lower(btrim(terminal_serial_number)))
    WHERE device_type = 'ASSISTED_PAYMENT_TERMINAL'
      AND lifecycle_status NOT IN ('REVOKED', 'REPLACED')
      AND terminal_serial_number IS NOT NULL;

COMMENT ON COLUMN identity.devices.terminal_serial_number IS
    'Persistent physical workstation serial number, distinct from terminal_identifier and immutable fiscal identities.';

COMMIT;
