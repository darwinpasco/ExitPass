DO $$
DECLARE
    missing_columns text[];
    missing_indexes text[];
    forbidden_columns text[];
BEGIN
    IF to_regclass('gates.hikcentral_gate_action_audits') IS NULL THEN
        RAISE EXCEPTION 'Missing table gates.hikcentral_gate_action_audits.';
    END IF;

    SELECT array_agg(column_name ORDER BY column_name)
    INTO missing_columns
    FROM (
        VALUES
            ('audit_id'),
            ('gate_command_id'),
            ('source_processing_id'),
            ('source_event_id'),
            ('exit_authorization_id'),
            ('gate_authorization_consumption_id'),
            ('parking_session_id'),
            ('payment_attempt_id'),
            ('tariff_snapshot_id'),
            ('gate_device_id'),
            ('gate_device_identifier'),
            ('door_index_code'),
            ('lane_id'),
            ('site_id'),
            ('vendor_system_id'),
            ('vendor_code'),
            ('vendor_name'),
            ('operation'),
            ('request_method'),
            ('request_path'),
            ('request_body_sha256'),
            ('signed_headers_list'),
            ('request_correlation_id'),
            ('vendor_request_id'),
            ('vendor_correlation_id'),
            ('http_status_code'),
            ('vendor_response_code'),
            ('vendor_response_message'),
            ('outcome_category'),
            ('retryable'),
            ('terminal_failure'),
            ('duration_ms'),
            ('timeout_occurred'),
            ('vendor_unavailable'),
            ('transport_error_code'),
            ('transport_error_message'),
            ('requested_at'),
            ('responded_at'),
            ('created_at')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns actual
        WHERE actual.table_schema = 'gates'
          AND actual.table_name = 'hikcentral_gate_action_audits'
          AND actual.column_name = expected.column_name
    );

    IF missing_columns IS NOT NULL THEN
        RAISE EXCEPTION 'Missing HikCentral audit columns: %', array_to_string(missing_columns, ', ');
    END IF;

    SELECT array_agg(index_name ORDER BY index_name)
    INTO missing_indexes
    FROM (
        VALUES
            ('ix_hikcentral_gate_action_audits_gate_command'),
            ('ix_hikcentral_gate_action_audits_source_processing'),
            ('ix_hikcentral_gate_action_audits_consumption'),
            ('ix_hikcentral_gate_action_audits_exit_authorization'),
            ('ix_hikcentral_gate_action_audits_vendor_response_code'),
            ('ix_hikcentral_gate_action_audits_outcome'),
            ('ix_hikcentral_gate_action_audits_created_requested')
    ) AS expected(index_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_indexes actual
        WHERE actual.schemaname = 'gates'
          AND actual.tablename = 'hikcentral_gate_action_audits'
          AND actual.indexname = expected.index_name
    );

    IF missing_indexes IS NOT NULL THEN
        RAISE EXCEPTION 'Missing HikCentral audit indexes: %', array_to_string(missing_indexes, ', ');
    END IF;

    SELECT array_agg(column_name ORDER BY column_name)
    INTO forbidden_columns
    FROM information_schema.columns
    WHERE table_schema = 'gates'
      AND table_name = 'hikcentral_gate_action_audits'
      AND column_name IN (
          'app_secret',
          'secret',
          'raw_secret',
          'x_ca_signature',
          'request_body',
          'raw_request_body',
          'response_body',
          'raw_response_body',
          'authorization_header'
      );

    IF forbidden_columns IS NOT NULL THEN
        RAISE EXCEPTION 'Forbidden secret/raw payload columns exist in HikCentral audit table: %',
            array_to_string(forbidden_columns, ', ');
    END IF;

    RAISE NOTICE 'HikCentral gate action audit schema validation passed.';
END $$;
