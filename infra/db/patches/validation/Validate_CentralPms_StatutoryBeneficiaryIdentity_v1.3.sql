DO $$
DECLARE
    missing_columns text[];
BEGIN
    IF to_regclass('discounts.statutory_beneficiary_identities') IS NULL THEN
        RAISE EXCEPTION 'Missing table discounts.statutory_beneficiary_identities';
    END IF;

    SELECT array_agg(required.column_name ORDER BY required.column_name)
      INTO missing_columns
      FROM (VALUES
          ('statutory_discount_decision_command_id'),
          ('site_id'),
          ('entitlement_type'),
          ('statutory_id_type'),
          ('protected_identity_envelope'),
          ('protection_key_reference'),
          ('protection_key_version'),
          ('envelope_format_version'),
          ('captured_by_actor_ref'),
          ('captured_by_channel'),
          ('reviewed_by_actor_ref'),
          ('reviewed_at'),
          ('approval_decision_reference'),
          ('correlation_reference'),
          ('is_final')
      ) AS required(column_name)
     WHERE NOT EXISTS (
         SELECT 1
           FROM information_schema.columns actual
          WHERE actual.table_schema = 'discounts'
            AND actual.table_name = 'statutory_beneficiary_identities'
            AND actual.column_name = required.column_name
     );

    IF missing_columns IS NOT NULL THEN
        RAISE EXCEPTION 'Missing statutory beneficiary identity columns: %', missing_columns;
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_trigger
         WHERE tgrelid = 'discounts.statutory_beneficiary_identities'::regclass
           AND tgname = 'trg_statutory_beneficiary_identity_final_immutable'
           AND NOT tgisinternal
    ) THEN
        RAISE EXCEPTION 'Missing final statutory beneficiary identity immutability trigger';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM discounts.statutory_beneficiary_identities
         WHERE octet_length(protected_identity_envelope) < 31
            OR protection_key_reference = ''
            OR protection_key_version = ''
            OR envelope_format_version <= 0
            OR captured_by_channel NOT IN ('OPERATOR_CONSOLE', 'MANAGEMENT_PLATFORM')
            OR last_updated_by_channel NOT IN ('OPERATOR_CONSOLE', 'MANAGEMENT_PLATFORM')
            OR (is_final AND (
                reviewed_by_actor_ref IS NULL OR reviewed_at IS NULL OR approval_decision_reference IS NULL
            ))
    ) THEN
        RAISE EXCEPTION 'Invalid statutory beneficiary identity persistence state';
    END IF;
END;
$$;

SELECT
    COUNT(*) AS identity_count,
    COUNT(*) FILTER (WHERE is_final) AS final_identity_count,
    COUNT(*) FILTER (WHERE NOT is_final) AS pending_identity_count
FROM discounts.statutory_beneficiary_identities;
