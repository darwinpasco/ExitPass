DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'identity'
          AND table_name = 'devices'
          AND column_name = 'terminal_serial_number'
          AND data_type = 'character varying'
          AND character_maximum_length = 128
    ) THEN
        RAISE EXCEPTION 'identity.devices.terminal_serial_number is missing or invalid';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'identity'
          AND tablename = 'devices'
          AND indexname = 'uq_devices__live_apt_terminal_serial_number'
    ) THEN
        RAISE EXCEPTION 'active APT terminal serial uniqueness index is missing';
    END IF;

    IF EXISTS (
        SELECT lower(btrim(terminal_serial_number))
        FROM identity.devices
        WHERE device_type = 'ASSISTED_PAYMENT_TERMINAL'
          AND lifecycle_status NOT IN ('REVOKED', 'REPLACED')
          AND terminal_serial_number IS NOT NULL
        GROUP BY lower(btrim(terminal_serial_number))
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'active APT terminal serial numbers are not unique';
    END IF;
END;
$$;
