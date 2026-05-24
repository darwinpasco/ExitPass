-- scripts/dev-data/webpay-payment-exit-audit-eventing-diagnostics.sql
-- Read-only diagnostics for WebPay PayMongo payment finality, exit authorization,
-- gate consumption, and available audit/event evidence.
--
-- Schema basis:
-- - live pg_dump / psql \d inspection of audit, events, payments, core, and gates schemas
-- - no schema mutations are performed by this script
--
-- Usage:
--   psql -v ticket_reference="WEBPAY-20260524-FRESH-001" -f scripts/dev-data/webpay-payment-exit-audit-eventing-diagnostics.sql

\if :{?ticket_reference}
\else
\set ticket_reference ''
\endif

WITH requested AS (
    SELECT NULLIF(:'ticket_reference', '') AS ticket_reference
),
fallback_ticket AS (
    SELECT
        ps.vendor_session_ref AS ticket_reference
    FROM core.parking_sessions ps
    LEFT JOIN core.payment_attempts pa
      ON pa.parking_session_id = ps.parking_session_id
    LEFT JOIN payments.provider_sessions provider_session
      ON provider_session.payment_attempt_id = pa.payment_attempt_id
    LEFT JOIN core.payment_confirmations pc
      ON pc.payment_attempt_id = pa.payment_attempt_id
    LEFT JOIN core.exit_authorizations ea
      ON ea.payment_attempt_id = pa.payment_attempt_id
    LEFT JOIN gates.gate_authorization_consumptions gac
      ON gac.exit_authorization_id = ea.exit_authorization_id
    CROSS JOIN requested r
    WHERE r.ticket_reference IS NULL
      AND ps.vendor_session_ref LIKE 'WEBPAY-%'
    ORDER BY
        CASE
            WHEN gac.consume_status = 'CONSUMED' THEN 0
            WHEN ea.authorization_status = 'ISSUED' THEN 1
            WHEN pc.payment_confirmation_id IS NOT NULL THEN 2
            WHEN pa.attempt_status = 'CONFIRMED' THEN 3
            WHEN provider_session.session_status = 'PAID' THEN 4
            ELSE 5
        END,
        COALESCE(gac.consumed_at, ea.issued_at, pc.confirmed_at, pa.finalized_at, provider_session.updated_at, ps.updated_at, ps.created_at) DESC,
        ps.vendor_session_ref DESC
    LIMIT 1
),
selected AS (
    SELECT
        COALESCE(r.ticket_reference, f.ticket_reference, '') AS selected_ticket_reference
    FROM requested r
    LEFT JOIN fallback_ticket f
      ON TRUE
)
SELECT selected_ticket_reference
FROM selected
\gset

\echo Selected ticket_reference: :selected_ticket_reference

SELECT
    :'selected_ticket_reference' AS selected_ticket_reference,
    NULLIF(:'ticket_reference', '') AS requested_ticket_reference,
    CASE
        WHEN :'selected_ticket_reference' = '' THEN 'NO_WEBPAY_TICKET_FOUND'
        WHEN EXISTS (
            SELECT 1
            FROM core.parking_sessions ps
            WHERE ps.vendor_session_ref = :'selected_ticket_reference'
        ) THEN 'TARGET_TICKET_FOUND'
        WHEN NULLIF(:'ticket_reference', '') IS NOT NULL THEN 'REQUESTED_TICKET_NOT_FOUND'
        ELSE 'FALLBACK_TICKET_NOT_FOUND'
    END AS diagnostic_message;

WITH target_session AS (
    SELECT
        ps.parking_session_id,
        ps.vendor_session_ref AS ticket_reference,
        ps.session_status,
        ps.correlation_id
    FROM core.parking_sessions ps
    WHERE ps.vendor_session_ref = :'selected_ticket_reference'
),
attempts AS (
    SELECT
        pa.payment_attempt_id,
        pa.parking_session_id,
        pa.attempt_status,
        pa.correlation_id,
        pr.provider_code
    FROM core.payment_attempts pa
    JOIN target_session ts
      ON ts.parking_session_id = pa.parking_session_id
    LEFT JOIN payments.payment_rails pr
      ON pr.payment_rail_id = pa.payment_rail_id
),
provider_sessions AS (
    SELECT
        ps.provider_session_id,
        ps.payment_attempt_id,
        ps.provider_session_ref,
        ps.provider_transaction_ref,
        ps.session_status,
        ps.correlation_id
    FROM payments.provider_sessions ps
    JOIN attempts a
      ON a.payment_attempt_id = ps.payment_attempt_id
),
callbacks AS (
    SELECT
        cb.provider_callback_id,
        cb.provider_session_id,
        cb.payment_attempt_id,
        cb.provider_event_ref,
        cb.provider_transaction_ref,
        cb.callback_type,
        cb.signature_valid,
        cb.verification_status,
        cb.processing_status,
        cb.failure_reason_code,
        cb.correlation_id
    FROM payments.provider_callbacks cb
    LEFT JOIN provider_sessions ps
      ON ps.provider_session_id = cb.provider_session_id
    LEFT JOIN attempts a
      ON a.payment_attempt_id = cb.payment_attempt_id
    WHERE ps.provider_session_id IS NOT NULL
       OR a.payment_attempt_id IS NOT NULL
),
confirmations AS (
    SELECT
        pc.payment_confirmation_id,
        pc.payment_attempt_id,
        pc.provider_transaction_ref,
        pc.confirmation_status,
        pc.correlation_id
    FROM core.payment_confirmations pc
    JOIN attempts a
      ON a.payment_attempt_id = pc.payment_attempt_id
),
authorizations AS (
    SELECT
        ea.exit_authorization_id,
        ea.parking_session_id,
        ea.payment_attempt_id,
        ea.payment_confirmation_id,
        ea.authorization_status,
        ea.correlation_id
    FROM core.exit_authorizations ea
    JOIN attempts a
      ON a.payment_attempt_id = ea.payment_attempt_id
),
consumptions AS (
    SELECT
        gac.gate_authorization_consumption_id,
        gac.exit_authorization_id,
        gac.consume_status,
        gac.consume_reason_code,
        gac.correlation_id
    FROM gates.gate_authorization_consumptions gac
    JOIN authorizations ea
      ON ea.exit_authorization_id = gac.exit_authorization_id
),
correlations AS (
    SELECT correlation_id FROM target_session WHERE correlation_id IS NOT NULL
    UNION SELECT correlation_id FROM attempts WHERE correlation_id IS NOT NULL
    UNION SELECT correlation_id FROM provider_sessions WHERE correlation_id IS NOT NULL
    UNION SELECT correlation_id FROM callbacks WHERE correlation_id IS NOT NULL
    UNION SELECT correlation_id FROM confirmations WHERE correlation_id IS NOT NULL
    UNION SELECT correlation_id FROM authorizations WHERE correlation_id IS NOT NULL
    UNION SELECT correlation_id FROM consumptions WHERE correlation_id IS NOT NULL
)
SELECT
    ts.ticket_reference,
    ts.parking_session_id,
    STRING_AGG(DISTINCT a.attempt_status::text, ', ' ORDER BY a.attempt_status::text) AS payment_attempt_status,
    STRING_AGG(DISTINCT ps.session_status::text, ', ' ORDER BY ps.session_status::text) AS provider_session_status,
    COUNT(DISTINCT pc.payment_confirmation_id) AS payment_confirmation_count,
    COUNT(DISTINCT cb.provider_callback_id) AS provider_callback_count,
    COUNT(DISTINCT cb.provider_event_ref) FILTER (WHERE cb.provider_event_ref IS NOT NULL) AS provider_webhook_event_count,
    COUNT(DISTINCT po.provider_outcome_id) AS provider_outcome_count,
    STRING_AGG(DISTINCT a.provider_code, ', ' ORDER BY a.provider_code) AS provider_code,
    MAX(ea.exit_authorization_id::text) AS exit_authorization_id,
    STRING_AGG(DISTINCT ea.authorization_status::text, ', ' ORDER BY ea.authorization_status::text) AS exit_authorization_status,
    STRING_AGG(DISTINCT gac.consume_status::text, ', ' ORDER BY gac.consume_status::text) AS gate_consume_status,
    COUNT(DISTINCT gac.gate_authorization_consumption_id) AS gate_consume_count,
    (
        SELECT COUNT(*)
        FROM (
            SELECT duplicate_callbacks.provider_event_ref
            FROM callbacks duplicate_callbacks
            WHERE duplicate_callbacks.provider_event_ref IS NOT NULL
            GROUP BY duplicate_callbacks.provider_event_ref
            HAVING COUNT(*) > 1
        ) duplicate_provider_event_groups
    ) AS duplicate_provider_event_group_count,
    (
        SELECT COUNT(*)
        FROM gates.gate_events ge
        JOIN authorizations auth
          ON auth.exit_authorization_id = ge.exit_authorization_id
    ) AS gate_event_row_count,
    (
        SELECT COUNT(*)
        FROM audit.audit_events ae
        WHERE ae.correlation_id IN (SELECT correlation_id FROM correlations)
           OR ae.target_entity_id IN (
                SELECT payment_attempt_id FROM attempts
                UNION SELECT payment_confirmation_id FROM confirmations
                UNION SELECT exit_authorization_id FROM authorizations
                UNION SELECT gate_authorization_consumption_id FROM consumptions
           )
           OR ae.related_entity_id IN (
                SELECT payment_attempt_id FROM attempts
                UNION SELECT payment_confirmation_id FROM confirmations
                UNION SELECT exit_authorization_id FROM authorizations
                UNION SELECT gate_authorization_consumption_id FROM consumptions
           )
    ) AS audit_event_row_count,
    (
        SELECT COUNT(*)
        FROM events.domain_events de
        WHERE de.correlation_id IN (SELECT correlation_id FROM correlations)
           OR de.aggregate_id IN (
                SELECT payment_attempt_id FROM attempts
                UNION SELECT payment_confirmation_id FROM confirmations
                UNION SELECT exit_authorization_id FROM authorizations
                UNION SELECT gate_authorization_consumption_id FROM consumptions
           )
    ) AS domain_event_row_count,
    (
        SELECT COUNT(*)
        FROM events.outbox_events oe
        WHERE oe.correlation_id IN (SELECT correlation_id FROM correlations)
           OR oe.aggregate_id IN (
                SELECT payment_attempt_id FROM attempts
                UNION SELECT payment_confirmation_id FROM confirmations
                UNION SELECT exit_authorization_id FROM authorizations
                UNION SELECT gate_authorization_consumption_id FROM consumptions
           )
    ) AS outbox_event_row_count,
    CASE
        WHEN (
            (
                SELECT COUNT(*)
                FROM gates.gate_events ge
                JOIN authorizations auth
                  ON auth.exit_authorization_id = ge.exit_authorization_id
            )
            + (
                SELECT COUNT(*)
                FROM audit.audit_events ae
                WHERE ae.correlation_id IN (SELECT correlation_id FROM correlations)
                   OR ae.target_entity_id IN (
                        SELECT payment_attempt_id FROM attempts
                        UNION SELECT payment_confirmation_id FROM confirmations
                        UNION SELECT exit_authorization_id FROM authorizations
                        UNION SELECT gate_authorization_consumption_id FROM consumptions
                   )
                   OR ae.related_entity_id IN (
                        SELECT payment_attempt_id FROM attempts
                        UNION SELECT payment_confirmation_id FROM confirmations
                        UNION SELECT exit_authorization_id FROM authorizations
                        UNION SELECT gate_authorization_consumption_id FROM consumptions
                   )
            )
            + (
                SELECT COUNT(*)
                FROM events.domain_events de
                WHERE de.correlation_id IN (SELECT correlation_id FROM correlations)
                   OR de.aggregate_id IN (
                        SELECT payment_attempt_id FROM attempts
                        UNION SELECT payment_confirmation_id FROM confirmations
                        UNION SELECT exit_authorization_id FROM authorizations
                        UNION SELECT gate_authorization_consumption_id FROM consumptions
                   )
            )
            + (
                SELECT COUNT(*)
                FROM events.outbox_events oe
                WHERE oe.correlation_id IN (SELECT correlation_id FROM correlations)
                   OR oe.aggregate_id IN (
                        SELECT payment_attempt_id FROM attempts
                        UNION SELECT payment_confirmation_id FROM confirmations
                        UNION SELECT exit_authorization_id FROM authorizations
                        UNION SELECT gate_authorization_consumption_id FROM consumptions
                   )
            )
        ) > 0
        THEN 'EVENT_AUDIT_OUTBOX_TABLES'
        ELSE 'BUSINESS_CONTROL_TABLES'
    END AS durable_evidence_source,
    'Existing business-control evidence is payments.provider_callbacks/provider_outcomes, core.payment_confirmations, core.exit_authorizations, and gates.gate_authorization_consumptions. Post-change Central PMS event publication also persists audit.audit_events, events.domain_events, events.outbox_events, and gates.gate_events.' AS durable_evidence_explanation,
    (
        COUNT(DISTINCT cb.provider_callback_id)
        + COUNT(DISTINCT po.provider_outcome_id)
        + COUNT(DISTINCT pc.payment_confirmation_id)
        + COUNT(DISTINCT ea.exit_authorization_id)
        + COUNT(DISTINCT gac.gate_authorization_consumption_id)
    ) AS business_control_evidence_count
FROM target_session ts
LEFT JOIN attempts a
  ON a.parking_session_id = ts.parking_session_id
LEFT JOIN provider_sessions ps
  ON ps.payment_attempt_id = a.payment_attempt_id
LEFT JOIN callbacks cb
  ON cb.provider_session_id = ps.provider_session_id OR cb.payment_attempt_id = a.payment_attempt_id
LEFT JOIN payments.provider_outcomes po
  ON po.payment_attempt_id = a.payment_attempt_id
LEFT JOIN confirmations pc
  ON pc.payment_attempt_id = a.payment_attempt_id
LEFT JOIN authorizations ea
  ON ea.payment_attempt_id = a.payment_attempt_id
LEFT JOIN consumptions gac
  ON gac.exit_authorization_id = ea.exit_authorization_id
GROUP BY ts.ticket_reference, ts.parking_session_id
ORDER BY ts.ticket_reference;
