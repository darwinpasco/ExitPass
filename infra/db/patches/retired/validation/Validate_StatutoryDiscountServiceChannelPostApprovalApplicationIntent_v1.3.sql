DO $$
BEGIN
    IF to_regclass('operator_console.statutory_discount_service_channel_reviews') IS NULL THEN
        RAISE EXCEPTION 'missing table operator_console.statutory_discount_service_channel_reviews';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'operator_console'
          AND table_name = 'statutory_discount_service_channel_reviews'
          AND column_name = 'statutory_discount_validation_id'
          AND is_nullable = 'YES'
    ) THEN
        RAISE EXCEPTION 'missing nullable statutory_discount_validation_id linkage column';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_stat_disc_svc_reviews__validation'
    ) THEN
        RAISE EXCEPTION 'missing fk_stat_disc_svc_reviews__validation';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'operator_console'
          AND tablename = 'statutory_discount_service_channel_reviews'
          AND indexname = 'ux_stat_disc_svc_reviews__validation'
    ) THEN
        RAISE EXCEPTION 'missing unique validation linkage index';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'operator_console'
          AND tablename = 'statutory_discount_service_channel_reviews'
          AND indexname = 'ix_stat_disc_svc_reviews__decision_validation'
    ) THEN
        RAISE EXCEPTION 'missing decision validation linkage index';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'operator_console'
          AND table_name = 'statutory_discount_service_channel_reviews'
          AND (
              column_name ILIKE '%base64%'
              OR column_name ILIKE '%image%'
              OR column_name ILIKE '%raw%'
              OR column_name ILIKE '%full%id%'
              OR column_name ILIKE '%identity_value%'
          )
    ) THEN
        RAISE EXCEPTION 'unsafe raw identity or evidence columns were introduced on service-channel reviews';
    END IF;
END $$;
