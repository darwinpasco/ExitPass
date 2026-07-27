DO $$
DECLARE
    v_missing text[];
    v_count integer;
BEGIN
    SELECT array_agg(name)
    INTO v_missing
    FROM (VALUES
        ('statutory_discount_service_channel_reviews')
    ) AS expected(name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.tables t
        WHERE t.table_schema = 'operator_console'
          AND t.table_name = expected.name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing service-channel statutory discount review tables: %', v_missing;
    END IF;

    SELECT array_agg(name)
    INTO v_missing
    FROM (VALUES
        ('statutory_discount_decision_command_id'),
        ('request_reference'),
        ('parking_session_id'),
        ('source_channel'),
        ('site_id'),
        ('site_group_id'),
        ('entitlement_type'),
        ('masked_id_reference'),
        ('evidence_references'),
        ('review_status'),
        ('reviewer_user_id'),
        ('reviewer_access_evaluation_id'),
        ('reviewer_decision'),
        ('intake_correlation_id'),
        ('submitted_at'),
        ('reviewed_at')
    ) AS expected(name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns c
        WHERE c.table_schema = 'operator_console'
          AND c.table_name = 'statutory_discount_service_channel_reviews'
          AND c.column_name = expected.name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing service-channel statutory discount review columns: %', v_missing;
    END IF;

    SELECT array_agg(name)
    INTO v_missing
    FROM (VALUES
        ('pk_stat_disc_service_channel_reviews'),
        ('fk_stat_disc_svc_reviews__decision_command'),
        ('ck_stat_disc_svc_reviews__source_channel'),
        ('ck_stat_disc_svc_reviews__review_status'),
        ('ck_stat_disc_svc_reviews__review_completion')
    ) AS expected(name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = expected.name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing service-channel statutory discount review constraints: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'operator_console'
          AND indexname = 'ix_stat_disc_svc_reviews__pending_queue'
    ) THEN
        RAISE EXCEPTION 'missing service-channel statutory discount pending review queue index';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'operator_console'
          AND table_name = 'statutory_discount_service_channel_reviews'
          AND (
              column_name ILIKE '%raw%'
              OR column_name ILIKE '%base64%'
              OR column_name ILIKE '%image%'
              OR column_name ILIKE '%full%id%'
              OR column_name ILIKE '%evidence_payload%'
          )
    ) THEN
        RAISE EXCEPTION 'service-channel review linkage introduced unsafe raw evidence or full-identity columns';
    END IF;

    SELECT COUNT(*)
    INTO v_count
    FROM information_schema.tables
    WHERE table_schema = 'discounts'
      AND table_name = 'statutory_discount_payable_basis_application_commands';

    IF v_count <> 1 THEN
        RAISE EXCEPTION 'canonical application-v1 command table posture changed unexpectedly';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'operator_console'
          AND table_name LIKE '%payable_basis_application%'
          AND table_name <> 'statutory_discount_service_channel_reviews'
    ) THEN
        RAISE EXCEPTION 'operator-console service-channel review patch introduced payable-basis application objects';
    END IF;

    RAISE NOTICE 'Service-channel Operator Console statutory discount review linkage validation passed.';
END $$;
