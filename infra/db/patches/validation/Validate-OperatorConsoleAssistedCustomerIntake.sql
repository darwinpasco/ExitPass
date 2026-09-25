-- Read-only validation for ExitPass v1.3 Operator Console assisted customer intake.
DO $validation$
DECLARE
    missing_count integer;
    unexpected_count integer;
BEGIN
    IF to_regclass('core.parking_session_invoice_customer_information') IS NULL THEN
        RAISE EXCEPTION 'core.parking_session_invoice_customer_information is missing';
    END IF;

    SELECT count(*) INTO missing_count
    FROM (VALUES
        ('parking_session_id','uuid',NULL::integer,'NO'),
        ('customer_name','character varying',160,'YES'),
        ('customer_address','character varying',300,'YES'),
        ('customer_tin','character varying',40,'YES'),
        ('business_style','character varying',160,'YES'),
        ('row_version','bigint',NULL::integer,'NO'),
        ('created_at','timestamp with time zone',NULL::integer,'NO'),
        ('created_by_user_id','uuid',NULL::integer,'YES'),
        ('created_by_service_identity_id','uuid',NULL::integer,'YES'),
        ('created_source_channel','character varying',64,'NO'),
        ('updated_at','timestamp with time zone',NULL::integer,'NO'),
        ('updated_by_user_id','uuid',NULL::integer,'YES'),
        ('updated_by_service_identity_id','uuid',NULL::integer,'YES'),
        ('updated_source_channel','character varying',64,'NO')
    ) AS required(column_name, data_type, maximum_length, is_nullable)
    WHERE NOT EXISTS (
        SELECT 1 FROM information_schema.columns column_definition
        WHERE column_definition.table_schema = 'core'
          AND column_definition.table_name = 'parking_session_invoice_customer_information'
          AND column_definition.column_name = required.column_name
          AND column_definition.data_type = required.data_type
          AND column_definition.character_maximum_length IS NOT DISTINCT FROM required.maximum_length
          AND column_definition.is_nullable = required.is_nullable);
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Parking-session customer-information table has % missing or mismatched columns', missing_count;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'core'
          AND table_name = 'parking_session_invoice_customer_information'
          AND column_name IN ('statutory_id_number', 'payment_attempt_id', 'payment_intent_id', 'payment_confirmation_id')
    ) THEN
        RAISE EXCEPTION 'Parking-session customer-information table contains prohibited payment/statutory authority columns';
    END IF;

    SELECT count(*) INTO missing_count
    FROM (VALUES
        ('pk_parking_session_invoice_customer_information'),
        ('fk_ps_invoice_customer_information__parking_session'),
        ('fk_ps_invoice_customer_information__created_user'),
        ('fk_ps_invoice_customer_information__created_service'),
        ('fk_ps_invoice_customer_information__updated_user'),
        ('fk_ps_invoice_customer_information__updated_service'),
        ('ck_ps_invoice_customer_information__has_value'),
        ('ck_ps_invoice_customer_information__customer_name'),
        ('ck_ps_invoice_customer_information__customer_address'),
        ('ck_ps_invoice_customer_information__customer_tin'),
        ('ck_ps_invoice_customer_information__business_style'),
        ('ck_ps_invoice_customer_information__row_version'),
        ('ck_ps_invoice_customer_information__created_actor'),
        ('ck_ps_invoice_customer_information__updated_actor'),
        ('ck_ps_invoice_customer_information__created_source'),
        ('ck_ps_invoice_customer_information__updated_source'),
        ('ck_ps_invoice_customer_information__timestamps')
    ) AS required(constraint_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_constraint constraint_definition
        WHERE constraint_definition.conrelid = 'core.parking_session_invoice_customer_information'::regclass
          AND constraint_definition.conname = required.constraint_name);
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Parking-session customer-information table has % missing constraints', missing_count;
    END IF;

    SELECT count(*) INTO unexpected_count
    FROM pg_indexes
    WHERE schemaname = 'core'
      AND tablename = 'parking_session_invoice_customer_information'
      AND indexname <> 'pk_parking_session_invoice_customer_information';
    IF unexpected_count <> 0 THEN
        RAISE EXCEPTION 'Parking-session customer-information table has % unapproved secondary indexes', unexpected_count;
    END IF;

    IF to_regclass('core.payment_attempt_invoice_customer_information') IS NULL THEN
        RAISE EXCEPTION 'Immutable payment-attempt customer-information snapshot table is missing';
    END IF;
    IF obj_description('core.payment_attempt_invoice_customer_information'::regclass) NOT LIKE '%not the editable parking-session authority%' THEN
        RAISE EXCEPTION 'Payment-attempt customer-information snapshot authority boundary is undocumented';
    END IF;

    SELECT count(*) INTO missing_count
    FROM (VALUES ('birth_date'), ('submitted_by_user_id')) AS required(column_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'operator_console'
          AND table_name = 'statutory_discount_service_channel_reviews'
          AND column_name = required.column_name);
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Statutory review projection has % missing assisted-intake columns', missing_count;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'discounts' AND table_name = 'statutory_discount_validations'
          AND column_name = 'birth_date' AND data_type = 'date'
    ) THEN
        RAISE EXCEPTION 'Approved statutory validation birth_date snapshot is missing';
    END IF;

    SELECT count(*) INTO missing_count
    FROM (VALUES
        ('fk_stat_disc_svc_reviews__submitted_user'),
        ('ck_stat_disc_svc_reviews__operator_submitter'),
        ('ck_stat_disc_svc_reviews__operator_birth_date')
    ) AS required(constraint_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'operator_console.statutory_discount_service_channel_reviews'::regclass
          AND conname = required.constraint_name);
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Statutory review projection has % missing assisted-intake constraints', missing_count;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'operator_console.statutory_discount_service_channel_reviews'::regclass
          AND conname = 'ck_stat_disc_svc_reviews__source_channel'
          AND pg_get_constraintdef(oid) LIKE '%WEBPAY%'
          AND pg_get_constraintdef(oid) LIKE '%ASSISTED_PAYMENT_TERMINAL%'
          AND pg_get_constraintdef(oid) LIKE '%OPERATOR_CONSOLE%'
    ) THEN
        RAISE EXCEPTION 'Statutory review source-channel constraint does not admit all three approved channels';
    END IF;

    SELECT count(*) INTO missing_count
    FROM (VALUES
        ('statutory-discounts.decision.submit.operator-console'),
        ('statutory-discounts.decision.read'),
        ('sales-invoice-customer-information.read'),
        ('sales-invoice-customer-information.manage')
    ) AS required(permission_code)
    WHERE NOT EXISTS (
        SELECT 1 FROM identity.permissions permission
        WHERE permission.permission_code = required.permission_code
          AND permission.permission_status = 'ACTIVE'
          AND permission.is_sensitive
          AND permission.requires_audit);
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Assisted-intake permission catalog has % missing or invalid permissions', missing_count;
    END IF;

    SELECT count(*) INTO missing_count
    FROM (VALUES
        ('statutory-discounts.decision.submit.operator-console'),
        ('statutory-discounts.decision.read'),
        ('sales-invoice-customer-information.read'),
        ('sales-invoice-customer-information.manage')
    ) AS required(permission_code)
    WHERE NOT EXISTS (
        SELECT 1
        FROM identity.roles role
        JOIN identity.role_permissions binding ON binding.role_id = role.role_id
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE role.role_code = 'SITE_OPERATOR'
          AND role.role_status = 'ACTIVE'
          AND role.role_provenance = 'CANONICAL_ROLE'
          AND binding.binding_status = 'ACTIVE'
          AND binding.effective_from <= now()
          AND (binding.effective_to IS NULL OR binding.effective_to > now())
          AND permission.permission_code = required.permission_code);
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'SITE_OPERATOR has % missing assisted-intake permission bindings', missing_count;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.roles role
        JOIN identity.role_permissions binding ON binding.role_id = role.role_id
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE role.role_code = 'SITE_OPERATOR'
          AND binding.binding_status = 'ACTIVE'
          AND permission.permission_code IN (
              'statutory-discounts.decision.approve',
              'statutory-discounts.decision.reject',
              'statutory-discounts.decision.review',
              'reconciliation.manage',
              'fiscal-reporting.z.generate',
              'user.manage')
    ) THEN
        RAISE EXCEPTION 'SITE_OPERATOR has prohibited review, finance, fiscal-close, or identity authority';
    END IF;

    RAISE NOTICE 'PASS: Operator Console assisted customer intake schema and RBAC validation succeeded.';
END
$validation$;
