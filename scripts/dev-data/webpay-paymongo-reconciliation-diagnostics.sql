-- scripts/dev-data/webpay-paymongo-reconciliation-diagnostics.sql
-- Read-only baseline reconciliation diagnostics for WebPay PayMongo QRPH/PHP payments.
--
-- Schema basis:
-- - live information_schema inspection of payments, core, gates, audit, events, and reconciliation schemas
-- - provider-side webhook evidence is persisted in payments.provider_callbacks
-- - provider-side paid evidence is persisted in payments.provider_sessions and payments.provider_outcomes
-- - no schema or payment state mutations are performed by this script
--
-- Usage:
--   psql -v ticket_reference="WEBPAY-20260524-FRESH-009" -f scripts/dev-data/webpay-paymongo-reconciliation-diagnostics.sql
--   psql -v from_date="2026-05-24" -v to_date="2026-05-24" -f scripts/dev-data/webpay-paymongo-reconciliation-diagnostics.sql

\if :{?ticket_reference}
\else
\set ticket_reference ''
\endif

\if :{?from_date}
\else
\set from_date ''
\endif

\if :{?to_date}
\else
\set to_date ''
\endif

\if :{?provider_code}
\else
\set provider_code 'PAYMONGO'
\endif

WITH requested AS (
    SELECT
        NULLIF(:'ticket_reference', '') AS requested_ticket_reference,
        NULLIF(:'from_date', '')::date AS requested_from_date,
        NULLIF(:'to_date', '')::date AS requested_to_date,
        COALESCE(NULLIF(:'provider_code', ''), 'PAYMONGO') AS requested_provider_code
),
latest_webpay_seed_date AS (
    SELECT
        MAX(TO_DATE((REGEXP_MATCH(ps.vendor_session_ref, '^WEBPAY-([0-9]{8})-'))[1], 'YYYYMMDD')) AS latest_seed_date
    FROM core.parking_sessions ps
    WHERE ps.vendor_session_ref ~ '^WEBPAY-[0-9]{8}-'
),
selected_scope AS (
    SELECT
        r.requested_ticket_reference,
        COALESCE(r.requested_from_date, r.requested_to_date, l.latest_seed_date, CURRENT_DATE) AS selected_from_date,
        COALESCE(r.requested_to_date, r.requested_from_date, l.latest_seed_date, CURRENT_DATE) AS selected_to_date,
        r.requested_provider_code AS selected_provider_code
    FROM requested r
    CROSS JOIN latest_webpay_seed_date l
)
SELECT
    COALESCE(requested_ticket_reference, '') AS selected_ticket_reference,
    selected_from_date::text AS selected_from_date,
    selected_to_date::text AS selected_to_date,
    selected_provider_code
FROM selected_scope
\gset

\echo Requested ticket_reference: :ticket_reference
\echo Selected ticket_reference: :selected_ticket_reference
\echo Selected from_date: :selected_from_date
\echo Selected to_date: :selected_to_date
\echo Selected provider_code: :selected_provider_code

SELECT
    NULLIF(:'ticket_reference', '') AS requested_ticket_reference,
    :'selected_from_date'::date AS selected_from_date,
    :'selected_to_date'::date AS selected_to_date,
    :'selected_provider_code' AS selected_provider_code,
    CASE
        WHEN NULLIF(:'ticket_reference', '') IS NOT NULL
         AND NOT EXISTS (
             SELECT 1
             FROM core.parking_sessions ps
             WHERE ps.vendor_session_ref = :'ticket_reference'
         ) THEN 'REQUESTED_TICKET_NOT_FOUND'
        WHEN NULLIF(:'ticket_reference', '') IS NOT NULL THEN 'REQUESTED_TICKET_FOUND'
        ELSE 'DATE_RANGE_SELECTED'
    END AS diagnostic_message;

WITH requested AS (
    SELECT
        NULLIF(:'ticket_reference', '') AS requested_ticket_reference,
        :'selected_from_date'::date AS selected_from_date,
        :'selected_to_date'::date AS selected_to_date,
        :'selected_provider_code' AS selected_provider_code
),
requested_missing AS (
    SELECT
        r.requested_ticket_reference
    FROM requested r
    WHERE r.requested_ticket_reference IS NOT NULL
      AND NOT EXISTS (
          SELECT 1
          FROM core.parking_sessions ps
          WHERE ps.vendor_session_ref = r.requested_ticket_reference
      )
),
target_sessions AS (
    SELECT
        ps.parking_session_id,
        ps.vendor_session_ref AS ticket_reference,
        ps.session_status AS parking_session_status,
        ps.entry_at,
        ps.correlation_id AS parking_session_correlation_id
    FROM core.parking_sessions ps
    CROSS JOIN requested r
    WHERE (
            r.requested_ticket_reference IS NOT NULL
            AND ps.vendor_session_ref = r.requested_ticket_reference
        )
       OR (
            r.requested_ticket_reference IS NULL
            AND ps.vendor_session_ref ~ '^WEBPAY-[0-9]{8}-'
            AND TO_DATE((REGEXP_MATCH(ps.vendor_session_ref, '^WEBPAY-([0-9]{8})-'))[1], 'YYYYMMDD')
                BETWEEN r.selected_from_date AND r.selected_to_date
        )
),
attempts AS (
    SELECT
        pa.payment_attempt_id,
        pa.parking_session_id,
        pa.payment_rail_id,
        pa.currency_code,
        pa.amount,
        pa.attempt_status,
        pa.requested_at,
        pa.expires_at,
        pa.finalized_at,
        pa.failure_reason_code,
        pa.correlation_id,
        pr.provider_code,
        pr.rail_code
    FROM core.payment_attempts pa
    JOIN target_sessions ts
      ON ts.parking_session_id = pa.parking_session_id
    LEFT JOIN payments.payment_rails pr
      ON pr.payment_rail_id = pa.payment_rail_id
    CROSS JOIN requested r
    WHERE COALESCE(pr.provider_code, r.selected_provider_code) = r.selected_provider_code
),
provider_sessions AS (
    SELECT
        ps.provider_session_id,
        ps.payment_attempt_id,
        ps.payment_rail_id,
        ps.provider_session_ref,
        ps.provider_transaction_ref,
        ps.session_status,
        ps.currency_code,
        ps.amount,
        ps.provider_created_at,
        ps.provider_expires_at,
        ps.correlation_id
    FROM payments.provider_sessions ps
    JOIN attempts a
      ON a.payment_attempt_id = ps.payment_attempt_id
),
provider_callbacks AS (
    SELECT
        cb.provider_callback_id,
        cb.payment_attempt_id,
        cb.provider_session_id,
        cb.provider_event_ref,
        cb.provider_transaction_ref,
        cb.callback_type,
        cb.signature_valid,
        cb.verification_status,
        cb.processing_status,
        cb.received_at,
        cb.processed_at,
        cb.failure_reason_code,
        cb.correlation_id
    FROM payments.provider_callbacks cb
    LEFT JOIN attempts a
      ON a.payment_attempt_id = cb.payment_attempt_id
    LEFT JOIN provider_sessions ps
      ON ps.provider_session_id = cb.provider_session_id
    WHERE a.payment_attempt_id IS NOT NULL
       OR ps.provider_session_id IS NOT NULL
),
provider_outcomes AS (
    SELECT
        po.provider_outcome_id,
        po.payment_attempt_id,
        po.provider_session_id,
        po.provider_callback_id,
        po.provider_transaction_ref,
        po.provider_outcome_status,
        po.provider_native_status,
        po.currency_code,
        po.amount,
        po.verified_at,
        po.reported_to_central_pms_at,
        po.central_pms_report_status,
        po.failure_reason_code,
        po.correlation_id
    FROM payments.provider_outcomes po
    LEFT JOIN attempts a
      ON a.payment_attempt_id = po.payment_attempt_id
    LEFT JOIN provider_sessions ps
      ON ps.provider_session_id = po.provider_session_id
    WHERE a.payment_attempt_id IS NOT NULL
       OR ps.provider_session_id IS NOT NULL
),
payment_confirmations AS (
    SELECT
        pc.payment_confirmation_id,
        pc.payment_attempt_id,
        pc.provider_outcome_id,
        pc.payment_rail_id,
        pc.provider_transaction_ref,
        pc.currency_code,
        pc.confirmed_amount,
        pc.confirmation_status,
        pc.verified_at,
        pc.confirmed_at,
        pc.correlation_id
    FROM core.payment_confirmations pc
    JOIN attempts a
      ON a.payment_attempt_id = pc.payment_attempt_id
),
exit_authorizations AS (
    SELECT
        ea.exit_authorization_id,
        ea.parking_session_id,
        ea.payment_attempt_id,
        ea.payment_confirmation_id,
        ea.authorization_status,
        ea.issued_at,
        ea.expires_at,
        ea.correlation_id
    FROM core.exit_authorizations ea
    JOIN target_sessions ts
      ON ts.parking_session_id = ea.parking_session_id
),
gate_consumptions AS (
    SELECT
        gac.gate_authorization_consumption_id,
        gac.exit_authorization_id,
        gac.gate_device_id,
        gac.site_id,
        gac.lane_id,
        gac.consume_status,
        gac.consume_reason_code,
        gac.consumed_at,
        gac.correlation_id
    FROM gates.gate_authorization_consumptions gac
    JOIN exit_authorizations ea
      ON ea.exit_authorization_id = gac.exit_authorization_id
),
session_rollup AS (
    SELECT
        ts.ticket_reference,
        ts.parking_session_id,
        MAX(a.payment_attempt_id::text) AS payment_attempt_id,
        MAX(ps.provider_session_id::text) AS provider_session_id,
        MAX(pc.payment_confirmation_id::text) AS payment_confirmation_id,
        MAX(ea.exit_authorization_id::text) AS exit_authorization_id,
        STRING_AGG(DISTINCT a.provider_code, ', ' ORDER BY a.provider_code) FILTER (WHERE a.provider_code IS NOT NULL) AS provider_code,
        STRING_AGG(DISTINCT a.rail_code, ', ' ORDER BY a.rail_code) FILTER (WHERE a.rail_code IS NOT NULL) AS rail_code,
        STRING_AGG(DISTINCT a.attempt_status::text, ', ' ORDER BY a.attempt_status::text) FILTER (WHERE a.attempt_status IS NOT NULL) AS payment_attempt_status,
        STRING_AGG(DISTINCT ps.session_status::text, ', ' ORDER BY ps.session_status::text) FILTER (WHERE ps.session_status IS NOT NULL) AS provider_session_status,
        STRING_AGG(DISTINCT pc.confirmation_status::text, ', ' ORDER BY pc.confirmation_status::text) FILTER (WHERE pc.confirmation_status IS NOT NULL) AS payment_confirmation_status,
        STRING_AGG(DISTINCT ea.authorization_status::text, ', ' ORDER BY ea.authorization_status::text) FILTER (WHERE ea.authorization_status IS NOT NULL) AS exit_authorization_status,
        STRING_AGG(DISTINCT gc.consume_status::text, ', ' ORDER BY gc.consume_status::text) FILTER (WHERE gc.consume_status IS NOT NULL) AS gate_consume_status,
        COUNT(DISTINCT a.payment_attempt_id) AS payment_attempt_count,
        COUNT(DISTINCT ps.provider_session_id) AS provider_session_count,
        COUNT(DISTINCT pc.payment_confirmation_id) AS payment_confirmation_count,
        COUNT(DISTINCT cb.provider_callback_id) AS provider_callback_count,
        COUNT(DISTINCT cb.provider_event_ref) FILTER (WHERE cb.provider_event_ref IS NOT NULL) AS provider_webhook_event_count,
        COUNT(DISTINCT po.provider_outcome_id) AS provider_outcome_count,
        COUNT(DISTINCT ea.exit_authorization_id) AS exit_authorization_count,
        COUNT(DISTINCT gc.gate_authorization_consumption_id) AS gate_consume_count,
        COUNT(DISTINCT gc.gate_authorization_consumption_id) FILTER (WHERE gc.consume_status = 'CONSUMED') AS gate_consumed_count,
        COALESCE(
            MAX(ps.provider_transaction_ref),
            MAX(po.provider_transaction_ref),
            MAX(cb.provider_transaction_ref),
            MAX(ps.provider_session_ref)
        ) AS provider_reference,
        COALESCE(
            MAX(po.provider_transaction_ref),
            MAX(pc.provider_transaction_ref),
            MAX(ps.provider_transaction_ref),
            MAX(cb.provider_transaction_ref)
        ) AS provider_transaction_reference,
        MAX(a.amount) AS amount,
        MAX(pc.confirmed_amount) AS confirmed_amount,
        COALESCE(MAX(po.amount), MAX(ps.amount)) AS provider_amount_minor_units,
        CASE
            WHEN COALESCE(BTRIM(MAX(po.currency_code)), BTRIM(MAX(ps.currency_code))) = BTRIM(MAX(a.currency_code))
             AND COALESCE(MAX(po.amount), MAX(ps.amount)) = MAX(a.amount) * 100
                THEN COALESCE(MAX(po.amount), MAX(ps.amount)) / 100
            ELSE COALESCE(MAX(po.amount), MAX(ps.amount))
        END AS provider_amount,
        BTRIM(MAX(a.currency_code)) AS currency_code,
        COALESCE(BTRIM(MAX(po.currency_code)), BTRIM(MAX(ps.currency_code))) AS provider_currency,
        MAX(a.expires_at) AS latest_payment_attempt_expires_at,
        MAX(a.requested_at) AS latest_payment_attempt_requested_at,
        MAX(a.finalized_at) AS latest_payment_attempt_finalized_at,
        MAX(pc.confirmed_at) AS latest_payment_confirmed_at,
        MAX(po.verified_at) AS latest_provider_verified_at,
        MAX(ea.issued_at) AS exit_authorization_issued_at,
        MAX(gc.consumed_at) AS latest_gate_consumed_at,
        COALESCE(
            MAX(a.correlation_id::text),
            MAX(ps.correlation_id::text),
            MAX(cb.correlation_id::text),
            MAX(po.correlation_id::text),
            MAX(pc.correlation_id::text),
            MAX(ea.correlation_id::text),
            MAX(gc.correlation_id::text),
            MAX(ts.parking_session_correlation_id::text)
        ) AS correlation_id,
        COUNT(DISTINCT cb.provider_event_ref) FILTER (WHERE cb.provider_event_ref IS NOT NULL) < COUNT(cb.provider_event_ref) AS has_duplicate_provider_event,
        COUNT(DISTINCT pc.payment_confirmation_id) > 1 AS has_duplicate_payment_confirmation,
        BOOL_OR(a.attempt_status = 'CONFIRMED') AS has_confirmed_attempt,
        BOOL_OR(ps.session_status = 'PAID') AS has_paid_provider_session,
        BOOL_OR(po.provider_outcome_status::text = 'CONFIRMED') AS has_succeeded_provider_outcome,
        BOOL_OR(a.attempt_status::text IN ('REQUESTED', 'PENDING_PROVIDER', 'PENDING_FINALIZATION') AND a.expires_at < NOW()) AS has_stale_pending_attempt
    FROM target_sessions ts
    LEFT JOIN attempts a
      ON a.parking_session_id = ts.parking_session_id
    LEFT JOIN provider_sessions ps
      ON ps.payment_attempt_id = a.payment_attempt_id
    LEFT JOIN provider_callbacks cb
      ON cb.payment_attempt_id = a.payment_attempt_id
      OR cb.provider_session_id = ps.provider_session_id
    LEFT JOIN provider_outcomes po
      ON po.payment_attempt_id = a.payment_attempt_id
      OR po.provider_session_id = ps.provider_session_id
    LEFT JOIN payment_confirmations pc
      ON pc.payment_attempt_id = a.payment_attempt_id
      OR pc.provider_outcome_id = po.provider_outcome_id
    LEFT JOIN exit_authorizations ea
      ON ea.parking_session_id = ts.parking_session_id
      OR ea.payment_attempt_id = a.payment_attempt_id
      OR ea.payment_confirmation_id = pc.payment_confirmation_id
    LEFT JOIN gate_consumptions gc
      ON gc.exit_authorization_id = ea.exit_authorization_id
    GROUP BY ts.ticket_reference, ts.parking_session_id
),
classified AS (
    SELECT
        sr.*,
        CASE
            WHEN sr.gate_consumed_count > 0 AND sr.payment_confirmation_count = 0
                THEN 'GATE_CONSUMED_WITHOUT_CONFIRMATION'
            WHEN sr.exit_authorization_count > 0 AND sr.payment_confirmation_count = 0
                THEN 'EXIT_AUTHORIZATION_WITHOUT_CONFIRMATION'
            WHEN sr.has_duplicate_payment_confirmation
                THEN 'DUPLICATE_PAYMENT_CONFIRMATION'
            WHEN sr.has_duplicate_provider_event
                THEN 'DUPLICATE_PROVIDER_EVENT'
            WHEN sr.confirmed_amount IS NOT NULL
             AND sr.provider_amount IS NOT NULL
             AND sr.confirmed_amount <> sr.provider_amount
                THEN 'AMOUNT_MISMATCH'
            WHEN sr.currency_code IS NOT NULL
             AND sr.provider_currency IS NOT NULL
             AND sr.currency_code <> sr.provider_currency
                THEN 'CURRENCY_MISMATCH'
            WHEN (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
             AND sr.payment_confirmation_count = 0
                THEN 'PROVIDER_PAID_EXITPASS_MISSING'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count > 0
             AND NOT (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome OR sr.provider_callback_count > 0)
                THEN 'EXITPASS_CONFIRMED_PROVIDER_MISSING'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count > 0
             AND sr.exit_authorization_count = 0
                THEN 'CONFIRMED_WITHOUT_EXIT_AUTHORIZATION'
            WHEN sr.has_stale_pending_attempt
                THEN 'STALE_PENDING_ATTEMPT'
            WHEN sr.provider_session_count > 0
             AND NOT (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
                THEN 'PENDING_PROVIDER_SESSION'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count = 1
             AND (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
             AND sr.provider_callback_count >= 1
             AND sr.exit_authorization_count >= 1
                THEN 'MATCHED'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count >= 1
             AND (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
                THEN 'EXITPASS_CONFIRMED_PROVIDER_PAID'
            ELSE 'INCONCLUSIVE'
        END AS reconciliation_classification,
        CASE
            WHEN sr.gate_consumed_count > 0 AND sr.payment_confirmation_count = 0
                THEN 'Gate consumption exists but no ExitPass payment confirmation is linked to this WebPay ticket.'
            WHEN sr.exit_authorization_count > 0 AND sr.payment_confirmation_count = 0
                THEN 'Exit authorization exists but no ExitPass payment confirmation is linked to this WebPay ticket.'
            WHEN sr.has_duplicate_payment_confirmation
                THEN 'More than one ExitPass payment confirmation is linked to this WebPay ticket.'
            WHEN sr.has_duplicate_provider_event
                THEN 'Provider callback evidence contains duplicate provider_event_ref values for this WebPay ticket.'
            WHEN sr.confirmed_amount IS NOT NULL
             AND sr.provider_amount IS NOT NULL
             AND sr.confirmed_amount <> sr.provider_amount
                THEN 'ExitPass confirmed amount does not match provider paid amount.'
            WHEN sr.currency_code IS NOT NULL
             AND sr.provider_currency IS NOT NULL
             AND sr.currency_code <> sr.provider_currency
                THEN 'ExitPass currency does not match provider currency.'
            WHEN (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
             AND sr.payment_confirmation_count = 0
                THEN 'Provider-side paid evidence exists but ExitPass has no payment confirmation.'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count > 0
             AND NOT (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome OR sr.provider_callback_count > 0)
                THEN 'ExitPass confirmed payment has no provider paid/session/callback evidence.'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count > 0
             AND sr.exit_authorization_count = 0
                THEN 'ExitPass payment is confirmed but no exit authorization is linked.'
            WHEN sr.has_stale_pending_attempt
                THEN 'A pending payment attempt is past expires_at.'
            WHEN sr.provider_session_count > 0
             AND NOT (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
                THEN 'Provider session exists but is not paid/succeeded.'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count = 1
             AND (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
             AND sr.provider_callback_count >= 1
             AND sr.exit_authorization_count >= 1
                THEN 'ExitPass confirmation, PayMongo provider evidence, exit authorization, and optional gate evidence are aligned.'
            WHEN sr.has_confirmed_attempt
             AND sr.payment_confirmation_count >= 1
             AND (sr.has_paid_provider_session OR sr.has_succeeded_provider_outcome)
                THEN 'ExitPass confirmed payment and provider paid evidence are aligned, but ancillary webhook/exit evidence is incomplete.'
            ELSE 'No decisive reconciliation state was found for this WebPay ticket.'
        END AS reconciliation_reason
    FROM session_rollup sr
),
missing_requested_row AS (
    SELECT
        rm.requested_ticket_reference AS ticket_reference,
        NULL::uuid AS parking_session_id,
        NULL::text AS payment_attempt_id,
        NULL::text AS provider_session_id,
        NULL::text AS payment_confirmation_id,
        0::bigint AS provider_webhook_event_count,
        0::bigint AS provider_callback_count,
        0::bigint AS provider_outcome_count,
        0::bigint AS payment_confirmation_count,
        :'selected_provider_code' AS provider_code,
        NULL::text AS rail_code,
        NULL::text AS provider_session_status,
        NULL::text AS payment_attempt_status,
        NULL::text AS payment_confirmation_status,
        NULL::text AS provider_reference,
        NULL::text AS provider_transaction_reference,
        NULL::numeric AS amount,
        NULL::numeric AS confirmed_amount,
        NULL::numeric AS provider_amount_minor_units,
        NULL::numeric AS provider_amount,
        NULL::text AS currency_code,
        NULL::text AS provider_currency,
        NULL::text AS exit_authorization_id,
        NULL::text AS exit_authorization_status,
        NULL::text AS gate_consume_status,
        0::bigint AS gate_consume_count,
        NULL::text AS correlation_id,
        'REQUESTED_TICKET_NOT_FOUND' AS reconciliation_classification,
        'The supplied ticket_reference was not found; no fallback ticket was selected.' AS reconciliation_reason
    FROM requested_missing rm
)
SELECT
    c.ticket_reference,
    c.parking_session_id,
    c.payment_attempt_id,
    c.provider_session_id,
    c.payment_confirmation_id,
    c.provider_webhook_event_count,
    c.provider_callback_count,
    c.provider_outcome_count,
    c.payment_confirmation_count,
    c.provider_code,
    c.rail_code,
    c.provider_session_status,
    c.payment_attempt_status,
    c.payment_confirmation_status,
    c.provider_reference,
    c.provider_transaction_reference,
    c.amount,
    c.confirmed_amount,
    c.provider_amount_minor_units,
    c.provider_amount,
    c.currency_code,
    c.provider_currency,
    c.exit_authorization_id,
    c.exit_authorization_status,
    c.gate_consume_status,
    c.gate_consume_count,
    c.correlation_id,
    c.reconciliation_classification,
    c.reconciliation_reason
FROM classified c
UNION ALL
SELECT
    mrt.ticket_reference,
    mrt.parking_session_id,
    mrt.payment_attempt_id,
    mrt.provider_session_id,
    mrt.payment_confirmation_id,
    mrt.provider_webhook_event_count,
    mrt.provider_callback_count,
    mrt.provider_outcome_count,
    mrt.payment_confirmation_count,
    mrt.provider_code,
    mrt.rail_code,
    mrt.provider_session_status,
    mrt.payment_attempt_status,
    mrt.payment_confirmation_status,
    mrt.provider_reference,
    mrt.provider_transaction_reference,
    mrt.amount,
    mrt.confirmed_amount,
    mrt.provider_amount_minor_units,
    mrt.provider_amount,
    mrt.currency_code,
    mrt.provider_currency,
    mrt.exit_authorization_id,
    mrt.exit_authorization_status,
    mrt.gate_consume_status,
    mrt.gate_consume_count,
    mrt.correlation_id,
    mrt.reconciliation_classification,
    mrt.reconciliation_reason
FROM missing_requested_row mrt
ORDER BY ticket_reference;
