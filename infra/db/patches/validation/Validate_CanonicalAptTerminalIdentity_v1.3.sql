DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'identity.devices'::regclass
          AND conname = 'ck_devices__apt_terminal_identifier_nonblank'
          AND convalidated
    ) THEN
        RAISE EXCEPTION 'Canonical APT terminal nonblank constraint is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_index index_definition
        JOIN pg_class index_relation ON index_relation.oid = index_definition.indexrelid
        WHERE index_definition.indrelid = 'identity.devices'::regclass
          AND index_relation.relname = 'uq_devices__live_apt_terminal_identifier'
          AND index_definition.indisunique
          AND pg_get_indexdef(index_definition.indexrelid) ILIKE '%lower(btrim%terminal_identifier%'
          AND pg_get_expr(index_definition.indpred, index_definition.indrelid) ILIKE '%ASSISTED_PAYMENT_TERMINAL%'
          AND pg_get_expr(index_definition.indpred, index_definition.indrelid) ILIKE '%REPLACED%'
          AND pg_get_expr(index_definition.indpred, index_definition.indrelid) ILIKE '%REVOKED%'
    ) THEN
        RAISE EXCEPTION 'Canonical live APT terminal identity index is missing or invalid';
    END IF;

    IF EXISTS (
        SELECT lower(btrim(terminal_identifier))
        FROM identity.devices
        WHERE device_type = 'ASSISTED_PAYMENT_TERMINAL'
          AND lifecycle_status NOT IN ('REPLACED', 'REVOKED')
        GROUP BY lower(btrim(terminal_identifier))
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Canonical live APT terminal identities are duplicated';
    END IF;
END
$$;
