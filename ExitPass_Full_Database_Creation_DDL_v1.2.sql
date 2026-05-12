-- ExitPass Full Database Creation DDL v1.2 draft 1.4
-- Corrected complete baseline generated for v1.2 reference-data execution.
-- Folded baseline includes reference-data idempotency unique constraints already present in the v1.2 DDL.
-- Enum update: identity.service_credential_type_enum now includes WEBHOOK_SECRET_REFERENCE for webhook-receiver service identities.
-- Generated from ExitPass_Full_Database_Creation_DDL_v1.2.sql with enum correction only; no reference data is inserted here.
-- Generated from ExitPass Database Design v1.2 draft 1.6 / locked v1.2 baseline.
-- PostgreSQL target. Review before production use; this is draft 1.4 with corrected semantic uniqueness and partial unique indexes aligned to Database Design v1.2 Section 13.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS core;
CREATE SCHEMA IF NOT EXISTS payments;
CREATE SCHEMA IF NOT EXISTS sessions;
CREATE SCHEMA IF NOT EXISTS coupons;
CREATE SCHEMA IF NOT EXISTS discounts;
CREATE SCHEMA IF NOT EXISTS gates;
CREATE SCHEMA IF NOT EXISTS operations;
CREATE SCHEMA IF NOT EXISTS reconciliation;
CREATE SCHEMA IF NOT EXISTS sites;
CREATE SCHEMA IF NOT EXISTS merchants;
CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS integration;
CREATE SCHEMA IF NOT EXISTS config;
CREATE SCHEMA IF NOT EXISTS events;

-- ============================================================
-- ENUM TYPES
-- ============================================================
DO $$ BEGIN
    CREATE TYPE audit.audit_change_type_enum AS ENUM ('CREATE', 'UPDATE', 'DELETE', 'ACTIVATE', 'SUSPEND', 'REVOKE', 'RETIRE', 'APPROVE', 'REJECT', 'CONFIGURE', 'ROTATE_CREDENTIAL_REFERENCE', 'CORRECT', 'MIGRATE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.audit_event_category_enum AS ENUM ('DOMAIN_STATE_CHANGE', 'ACCESS', 'CONFIGURATION_CHANGE', 'POLICY_CHANGE', 'SECURITY_RELEVANT', 'INTEGRATION', 'RECONCILIATION', 'MANUAL_OPERATION', 'EVIDENCE_ACCESS', 'EVENTING', 'SYSTEM');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.audit_event_result_enum AS ENUM ('SUCCESS', 'FAILED', 'DENIED', 'REJECTED', 'EXPIRED', 'CANCELLED', 'DUPLICATE', 'NO_OP', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.evidence_access_classification_enum AS ENUM ('INTERNAL', 'RESTRICTED', 'HIGHLY_RESTRICTED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.evidence_link_status_enum AS ENUM ('ACTIVE', 'REDACTED', 'PURGED', 'HASH_ONLY', 'REVOKED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.evidence_redaction_status_enum AS ENUM ('NOT_REDACTED', 'PARTIALLY_REDACTED', 'FULLY_REDACTED', 'HASH_ONLY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.evidence_storage_type_enum AS ENUM ('OBJECT_STORAGE', 'EVIDENCE_VAULT', 'HASH_ONLY', 'EXTERNAL_REFERENCE', 'REDACTED_REFERENCE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.evidence_type_enum AS ENUM ('PROVIDER_PAYLOAD', 'PROVIDER_RESPONSE', 'STATUTORY_DISCOUNT_EVIDENCE', 'MANUAL_GATE_EVIDENCE', 'INCIDENT_EVIDENCE', 'RECONCILIATION_EVIDENCE', 'SETTLEMENT_EVIDENCE', 'CONFIGURATION_CHANGE_EVIDENCE', 'SECURITY_EVIDENCE', 'SCREENSHOT', 'DOCUMENT', 'HASH_ONLY_REFERENCE', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.security_event_category_enum AS ENUM ('AUTHENTICATION', 'AUTHORIZATION', 'REPLAY', 'RATE_LIMIT', 'WEBHOOK_TRUST', 'TOKEN_VALIDATION', 'EVIDENCE_ACCESS', 'PRIVILEGED_ACCESS', 'CREDENTIAL_REFERENCE', 'SERVICE_IDENTITY', 'SUSPICIOUS_ACTIVITY', 'DATA_ACCESS', 'CONFIGURATION_SECURITY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.security_event_result_enum AS ENUM ('ALLOWED', 'DENIED', 'FAILED', 'BLOCKED', 'REJECTED', 'DETECTED', 'ESCALATED', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.security_event_status_enum AS ENUM ('OPEN', 'ACKNOWLEDGED', 'UNDER_REVIEW', 'RESOLVED', 'FALSE_POSITIVE', 'ESCALATED', 'CLOSED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE audit.security_severity_enum AS ENUM ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.controlled_code_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'DEPRECATED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.feature_flag_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'EXPIRED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.rate_limit_enforcement_mode_enum AS ENUM ('MONITOR_ONLY', 'ENFORCE', 'BLOCK', 'CHALLENGE', 'DISABLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.rate_limit_policy_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.rate_limit_scope_type_enum AS ENUM ('PUBLIC_LOOKUP', 'PAYMENT_CREATE', 'PROVIDER_CALLBACK', 'GATE_CONSUME', 'ADMIN_API', 'SUPPORT_TOOL', 'EVIDENCE_ACCESS', 'SERVICE_TO_SERVICE', 'MERCHANT_USER', 'DEVICE', 'CUSTOM');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.system_parameter_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'SUPERSEDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.system_parameter_type_enum AS ENUM ('TEXT', 'NUMERIC', 'BOOLEAN', 'JSON_REFERENCE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.ttl_expiry_action_enum AS ENUM ('EXPIRE_RECORD', 'INVALIDATE_RECORD', 'RELEASE_RESERVATION', 'REQUIRE_RECHECK', 'BLOCK_USE', 'PURGE_OR_ARCHIVE', 'NOTIFY_ONLY', 'CUSTOM_WORKFLOW');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.ttl_policy_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE config.ttl_scope_type_enum AS ENUM ('TARIFF_SNAPSHOT', 'PAYMENT_ATTEMPT', 'PROVIDER_SESSION', 'COUPON_RESERVATION', 'STATUTORY_DISCOUNT_VALIDATION', 'SESSION_LOOKUP_CACHE', 'EXIT_AUTHORIZATION', 'PROVIDER_CALLBACK_REPLAY_WINDOW', 'EVIDENCE_RETENTION', 'OUTBOX_RETRY', 'CUSTOM');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE core.exit_authorization_status_enum AS ENUM ('ISSUED', 'EXPIRED', 'INVALIDATED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE core.parking_session_status_enum AS ENUM ('ACTIVE', 'CLOSED', 'EXPIRED', 'INVALIDATED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE core.payment_attempt_status_enum AS ENUM ('REQUESTED', 'PENDING_PROVIDER', 'PENDING_FINALIZATION', 'CONFIRMED', 'FAILED', 'EXPIRED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE core.payment_confirmation_status_enum AS ENUM ('RECORDED', 'VOIDED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE core.tariff_snapshot_status_enum AS ENUM ('ACTIVE', 'CONSUMED', 'EXPIRED', 'SUPERSEDED', 'INVALIDATED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_application_status_enum AS ENUM ('REQUESTED', 'RESERVED', 'APPLIED', 'COMMITTED', 'RELEASED', 'EXPIRED', 'REJECTED', 'CANCELLED', 'REVERSED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_denomination_type_enum AS ENUM ('FIXED_AMOUNT', 'PERCENTAGE', 'FULL_WAIVER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_rule_evaluation_strategy_enum AS ENUM ('ALL_RULES_MUST_PASS', 'ANY_RULE_MAY_PASS', 'FIRST_MATCH', 'PRIORITY_ORDERED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_rule_group_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_rule_operator_enum AS ENUM ('EQUALS', 'NOT_EQUALS', 'IN', 'NOT_IN', 'GREATER_THAN', 'GREATER_THAN_OR_EQUAL', 'LESS_THAN', 'LESS_THAN_OR_EQUAL', 'BETWEEN', 'EXISTS', 'NOT_EXISTS');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_rule_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_rule_type_enum AS ENUM ('SITE_GROUP_SCOPE', 'SITE_SCOPE', 'MERCHANT_SCOPE', 'VALIDITY_WINDOW', 'MINIMUM_GROSS_AMOUNT', 'MAXIMUM_DISCOUNT_AMOUNT', 'STACKING_POLICY', 'FULL_WAIVER_ALLOWED', 'WALLET_SUFFICIENCY', 'BASELINE_HOURS_ONLY', 'PAYMENT_RAIL_SCOPE', 'CUSTOM_CONTROLLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_stacking_policy_enum AS ENUM ('NO_STACKING', 'STACK_WITH_STATUTORY_DISCOUNT', 'STACK_WITH_COUPON', 'STACK_WITH_BOTH', 'HIGHEST_BENEFIT_ONLY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'EXPIRED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE coupons.coupon_type_enum AS ENUM ('STANDARD', 'MERCHANT_SUBSIDY', 'VALIDATION', 'FULL_WAIVER', 'SERVICE_RECOVERY', 'PROMOTIONAL');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.discount_evidence_type_enum AS ENUM ('SENIOR_CITIZEN_ID', 'PWD_ID', 'AUTHORIZATION_LETTER', 'SUPPORTING_DOCUMENT', 'VALIDATION_SCREENSHOT', 'HASH_ONLY_REFERENCE', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.discount_policy_level_enum AS ENUM ('NATIONAL_LAW', 'LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.discount_policy_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'SUPERSEDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.discount_policy_type_enum AS ENUM ('LEGAL_REFERENCE', 'LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY', 'IMPLEMENTATION_POLICY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.evidence_access_classification_enum AS ENUM ('INTERNAL', 'RESTRICTED', 'HIGHLY_RESTRICTED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.evidence_capture_status_enum AS ENUM ('CAPTURED', 'REFERENCED', 'REDACTED', 'PURGED', 'HASH_ONLY', 'REJECTED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.evidence_redaction_status_enum AS ENUM ('NOT_REDACTED', 'PARTIALLY_REDACTED', 'FULLY_REDACTED', 'HASH_ONLY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.evidence_storage_type_enum AS ENUM ('OBJECT_STORAGE', 'EVIDENCE_VAULT', 'HASH_ONLY', 'EXTERNAL_REFERENCE', 'REDACTED_REFERENCE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.policy_resolution_basis_enum AS ENUM ('LOCAL_ORDINANCE_APPLIED', 'NATIONAL_LAW_FALLBACK', 'SITE_POLICY_OPERATIONAL_ONLY', 'MANUAL_POLICY_SELECTION', 'SYSTEM_DEFAULT');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.statutory_discount_validations_channel_enum AS ENUM ('WEB_PAY', 'OPERATOR_ASSISTED', 'SYSTEM_VALIDATED', 'SUPPORT_REVIEW', 'RECONCILIATION_REVIEW');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.statutory_discount_validations_status_enum AS ENUM ('REQUESTED', 'PENDING_OPERATOR_REVIEW', 'APPROVED', 'REJECTED', 'FAILED', 'EXPIRED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE discounts.statutory_entitlement_type_enum AS ENUM ('SENIOR_CITIZEN', 'PWD', 'OTHER_STATUTORY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE events.consumer_checkpoint_status_enum AS ENUM ('ACTIVE', 'LOCKED', 'FAILED', 'PAUSED', 'REPLAYING', 'RESET', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE events.dead_letter_status_enum AS ENUM ('OPEN', 'UNDER_REVIEW', 'REPLAY_REQUESTED', 'REPLAYED', 'RESOLVED', 'REJECTED', 'CLOSED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE events.dead_letter_type_enum AS ENUM ('PUBLICATION_FAILURE', 'BROKER_REJECTION', 'PAYLOAD_VALIDATION_FAILURE', 'ROUTING_FAILURE', 'CONSUMER_FAILURE', 'CONSUMER_REJECTION', 'RETRY_EXHAUSTED', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE events.domain_event_status_enum AS ENUM ('RECORDED', 'SUPERSEDED', 'CANCELLED', 'IGNORED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE events.event_broker_type_enum AS ENUM ('RABBITMQ', 'KAFKA', 'AZURE_SERVICE_BUS', 'AWS_SNS_SQS', 'WEBHOOK', 'IN_PROCESS', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE events.event_publication_status_enum AS ENUM ('STARTED', 'PUBLISHED', 'FAILED', 'TIMEOUT', 'REJECTED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE events.outbox_publication_status_enum AS ENUM ('PENDING', 'LOCKED', 'PUBLISHED', 'FAILED', 'RETRY_PENDING', 'DEAD_LETTERED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE gates.gate_authorization_consumption_status_enum AS ENUM ('REQUESTED', 'VALIDATED', 'CONSUMED', 'DENIED', 'EXPIRED', 'INVALID', 'REPLAYED', 'MISMATCHED', 'FAILED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE gates.gate_command_result_status_enum AS ENUM ('NOT_REQUESTED', 'REQUESTED', 'ACKNOWLEDGED', 'OPENED', 'FAILED', 'TIMEOUT', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE gates.gate_device_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'MAINTENANCE', 'OFFLINE', 'SUSPENDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE gates.gate_device_type_enum AS ENUM ('BARRIER_CONTROLLER', 'LANE_CONTROLLER', 'EXIT_TERMINAL', 'LPR_DEVICE', 'GATEWAY', 'INTEGRATION_ENDPOINT', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE gates.gate_event_status_enum AS ENUM ('RECORDED', 'SUCCESS', 'FAILED', 'ERROR', 'ABNORMAL', 'IGNORED', 'DUPLICATE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE gates.gate_event_type_enum AS ENUM ('AUTHORIZATION_PRESENTED', 'AUTHORIZATION_VALIDATED', 'AUTHORIZATION_DENIED', 'AUTHORIZATION_CONSUMED', 'GATE_OPEN_COMMAND_REQUESTED', 'GATE_OPEN_ACKNOWLEDGED', 'GATE_OPEN_FAILED', 'BARRIER_RAISED', 'BARRIER_LOWERED', 'VEHICLE_DETECTED', 'VEHICLE_EXITED', 'DEVICE_ONLINE', 'DEVICE_OFFLINE', 'DEVICE_ERROR', 'MANUAL_INTERVENTION', 'TAMPER_ALERT', 'ABNORMAL_EVENT');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE gates.gate_heartbeat_status_enum AS ENUM ('ONLINE', 'DEGRADED', 'OFFLINE', 'ERROR', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.permission_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'DEPRECATED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.role_permission_binding_status_enum AS ENUM ('ACTIVE', 'SUSPENDED', 'REVOKED', 'EXPIRED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.role_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.role_type_enum AS ENUM ('SYSTEM', 'OPERATIONS', 'MERCHANT', 'FINANCE', 'COMPLIANCE', 'SUPPORT', 'SECURITY', 'SERVICE', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.service_credential_type_enum AS ENUM ('CLIENT_SECRET_REFERENCE', 'CERTIFICATE_REFERENCE', 'MTLS_CERTIFICATE_REFERENCE', 'API_KEY_REFERENCE', 'JWT_SIGNING_KEY_REFERENCE', 'KEY_VAULT_REFERENCE', 'WEBHOOK_SECRET_REFERENCE', 'NONE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.service_identity_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'REVOKED', 'EXPIRED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.service_identity_type_enum AS ENUM ('INTERNAL_SERVICE', 'EXTERNAL_CLIENT', 'ADAPTER', 'BACKGROUND_WORKER', 'SCHEDULED_JOB', 'WEBHOOK_RECEIVER', 'DEVICE', 'GATEWAY', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.user_role_assignment_status_enum AS ENUM ('ACTIVE', 'SUSPENDED', 'REVOKED', 'EXPIRED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.user_status_enum AS ENUM ('INVITED', 'ACTIVE', 'LOCKED', 'SUSPENDED', 'INACTIVE', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.user_type_enum AS ENUM ('INTERNAL_ADMIN', 'OPERATIONS_USER', 'SITE_OPERATOR', 'SUPPORT_USER', 'FINANCE_USER', 'COMPLIANCE_USER', 'MERCHANT_USER', 'SECURITY_USER', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.adapter_mapping_confidence_enum AS ENUM ('MANUAL_APPROVED', 'IMPORTED_APPROVED', 'SYSTEM_DISCOVERED', 'LOW_CONFIDENCE', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.adapter_mapping_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'SUPERSEDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.adapter_mapping_type_enum AS ENUM ('SITE_GROUP', 'SITE', 'LANE', 'GATE_DEVICE', 'PAYMENT_RAIL', 'TARIFF_GROUP', 'VENDOR_LOCATION', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.http_method_enum AS ENUM ('GET', 'POST', 'PUT', 'PATCH', 'DELETE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.integration_credential_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'ROTATION_DUE', 'EXPIRED', 'REVOKED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.integration_credential_type_enum AS ENUM ('API_KEY_REFERENCE', 'CLIENT_SECRET_REFERENCE', 'OAUTH_CLIENT_REFERENCE', 'MTLS_CERTIFICATE_REFERENCE', 'SIGNING_KEY_REFERENCE', 'WEBHOOK_SECRET_REFERENCE', 'BASIC_AUTH_REFERENCE', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.integration_health_check_type_enum AS ENUM ('SCHEDULED_HEALTH_CHECK', 'ON_DEMAND_CHECK', 'REQUEST_FAILURE', 'CALLBACK_FAILURE', 'LATENCY_OBSERVATION', 'RECOVERY_OBSERVATION', 'MANUAL_OBSERVATION');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.integration_health_status_enum AS ENUM ('AVAILABLE', 'DEGRADED', 'UNAVAILABLE', 'ERROR', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.secret_store_type_enum AS ENUM ('KEY_VAULT', 'SECRETS_MANAGER', 'CERTIFICATE_STORE', 'HSM', 'ENVIRONMENT_REFERENCE', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.vendor_endpoint_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'MAINTENANCE', 'SUSPENDED', 'DEPRECATED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.vendor_endpoint_type_enum AS ENUM ('SESSION_LOOKUP', 'TARIFF_QUERY', 'PAYMENT_CREATE', 'PAYMENT_STATUS', 'WEBHOOK_RECEIVE', 'GATE_COMMAND', 'HEALTH_CHECK', 'TOKEN_REQUEST', 'EVIDENCE_UPLOAD', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.vendor_system_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'MAINTENANCE', 'SUSPENDED', 'DEPRECATED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE integration.vendor_system_type_enum AS ENUM ('VENDOR_PMS', 'PAYMENT_PROVIDER', 'GATE_CONTROLLER', 'LPR_PROVIDER', 'MOPS_PROVIDER', 'EVIDENCE_STORAGE', 'NOTIFICATION_PROVIDER', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_scope_type_enum AS ENUM ('SITE_GROUP', 'SITE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_site_scope_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'EXPIRED', 'REVOKED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'INACTIVE', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_type_enum AS ENUM ('TENANT', 'ANCHOR_TENANT', 'PROPERTY_OPERATOR', 'INSTITUTION', 'SERVICE_PROVIDER', 'PROMOTIONAL_PARTNER', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_user_status_enum AS ENUM ('INVITED', 'ACTIVE', 'SUSPENDED', 'REVOKED', 'EXPIRED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_user_type_enum AS ENUM ('MERCHANT_ADMIN', 'MERCHANT_MANAGER', 'MERCHANT_STAFF', 'REPORT_VIEWER', 'COUPON_OPERATOR', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_wallet_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'FROZEN', 'CLOSED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE merchants.merchant_wallet_type_enum AS ENUM ('PRE_FUNDED', 'POSTPAID_SPONSORSHIP', 'CREDIT_LIMIT', 'EXTERNAL_LEDGER', 'PROMOTIONAL_BUDGET', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.incident_severity_enum AS ENUM ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.incident_status_enum AS ENUM ('OPEN', 'ACKNOWLEDGED', 'INVESTIGATING', 'MITIGATED', 'RESOLVED', 'CLOSED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.manual_gate_action_status_enum AS ENUM ('RECORDED', 'COMPLETED', 'FAILED', 'CANCELLED', 'UNDER_REVIEW', 'RECONCILED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.manual_gate_action_type_enum AS ENUM ('MANUAL_OPEN', 'MANUAL_RELEASE', 'EMERGENCY_OPEN', 'MOPS_RELEASE', 'SUPERVISOR_RELEASE', 'DEVICE_FAILURE_RELEASE', 'INCIDENT_RELEASE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.operator_action_status_enum AS ENUM ('RECORDED', 'SUCCESS', 'FAILED', 'DENIED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.operator_action_type_enum AS ENUM ('SENSITIVE_EVIDENCE_VIEW', 'INCIDENT_ASSIGN', 'INCIDENT_UPDATE', 'RECONCILIATION_REVIEW', 'CONTROLLED_RECHECK', 'PROVIDER_STATUS_QUERY_TRIGGERED', 'SUPPORT_NOTE_ADDED', 'REPORT_EXPORTED', 'CONFIGURATION_VIEW', 'SECURITY_REVIEW');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.override_approval_decision_enum AS ENUM ('APPROVED', 'REJECTED', 'ESCALATED', 'CANCELLED', 'EXPIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.override_request_status_enum AS ENUM ('REQUESTED', 'PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'CANCELLED', 'EXPIRED', 'EXECUTED', 'CLOSED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE operations.override_type_enum AS ENUM ('MANUAL_GATE_OPEN', 'EXIT_AUTHORIZATION_REISSUE', 'COUPON_EXCEPTION', 'STATUTORY_DISCOUNT_REVIEW_EXCEPTION', 'INCIDENT_RELEASE', 'CONTINUITY_ACTION', 'RECONCILIATION_CORRECTION', 'SUPPORT_ACTION');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.central_pms_report_status_enum AS ENUM ('NOT_REPORTED', 'REPORTED', 'ACCEPTED', 'REJECTED', 'FAILED', 'RETRY_PENDING');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.payment_rail_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'MAINTENANCE', 'DEPRECATED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.payment_rail_type_enum AS ENUM ('QRPH', 'CARD', 'EWALLET', 'HOSTED_CHECKOUT', 'BANK_TRANSFER', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.provider_callback_processing_status_enum AS ENUM ('RECEIVED', 'PROCESSING', 'PROCESSED', 'DUPLICATE', 'REJECTED', 'FAILED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.provider_callback_verification_status_enum AS ENUM ('UNVERIFIED', 'VERIFIED', 'FAILED_SIGNATURE', 'FAILED_TIMESTAMP', 'FAILED_SOURCE', 'FAILED_REPLAY', 'FAILED_SCHEMA', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.provider_outcome_status_enum AS ENUM ('CONFIRMED', 'FAILED', 'EXPIRED', 'CANCELLED', 'REJECTED', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.provider_session_status_enum AS ENUM ('CREATED', 'ACTIVE', 'PENDING', 'PAID', 'FAILED', 'EXPIRED', 'CANCELLED', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE payments.provider_status_query_status_enum AS ENUM ('REQUESTED', 'COMPLETED', 'FAILED', 'TIMEOUT', 'INCONCLUSIVE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.mops_transaction_record_status_enum AS ENUM ('RECORDED', 'IMPORTED', 'PENDING_RECONCILIATION', 'RECONCILED', 'DISPUTED', 'REJECTED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_comparison_basis_enum AS ENUM ('MOPS_TO_CORE', 'MOPS_TO_SETTLEMENT', 'PROVIDER_TO_CORE', 'MANUAL_GATE_TO_CORE', 'COUPON_WALLET_TO_APPLICATION', 'SETTLEMENT_TO_CONFIRMATION', 'INCIDENT_SCOPE_REVIEW');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_exception_severity_enum AS ENUM ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_exception_status_enum AS ENUM ('OPEN', 'ASSIGNED', 'UNDER_REVIEW', 'RESOLVED', 'REJECTED', 'ESCALATED', 'CLOSED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_exception_type_enum AS ENUM ('AMOUNT_MISMATCH', 'MISSING_PAYMENT_CONFIRMATION', 'MISSING_PROVIDER_OUTCOME', 'MISSING_MOPS_RECORD', 'DUPLICATE_RECORD', 'MANUAL_GATE_WITHOUT_PAYMENT', 'SETTLEMENT_MISMATCH', 'COUPON_WALLET_MISMATCH', 'UNRESOLVED_CONTINUITY_RECORD', 'POLICY_EXCEPTION', 'UNKNOWN_EXCEPTION');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_item_status_enum AS ENUM ('PENDING', 'MATCHED', 'MISMATCHED', 'EXCEPTION', 'DISPUTED', 'REJECTED', 'RESOLVED', 'CLOSED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_match_status_enum AS ENUM ('NOT_EVALUATED', 'MATCH', 'AMOUNT_MISMATCH', 'MISSING_SOURCE', 'MISSING_TARGET', 'DUPLICATE', 'INCONCLUSIVE', 'REJECTED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_run_status_enum AS ENUM ('STARTED', 'PROCESSING', 'COMPLETED', 'FAILED', 'CANCELLED', 'REPROCESSING');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_run_type_enum AS ENUM ('MOPS_RECONCILIATION', 'PROVIDER_SETTLEMENT', 'MANUAL_GATE_REVIEW', 'INCIDENT_RECONCILIATION', 'COUPON_WALLET_RECONCILIATION', 'PAYMENT_PROVIDER_RECONCILIATION', 'VENDOR_PMS_RECONCILIATION');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.reconciliation_scope_type_enum AS ENUM ('TIME_WINDOW', 'SITE', 'SITE_GROUP', 'INCIDENT', 'SOURCE_BATCH', 'PAYMENT_RAIL', 'VENDOR_SYSTEM', 'MIXED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.settlement_comparison_result_enum AS ENUM ('MATCHED', 'MISMATCHED', 'SHORT_SETTLED', 'OVER_SETTLED', 'MISSING_SETTLEMENT', 'DUPLICATE_SETTLEMENT', 'UNRESOLVED', 'REJECTED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE reconciliation.settlement_comparison_source_type_enum AS ENUM ('PROVIDER_SETTLEMENT_REPORT', 'BANK_STATEMENT', 'PAYMENT_RAIL_REPORT', 'MERCHANT_WALLET_LEDGER', 'MOPS_EXPORT', 'MANUAL_COLLECTION_REPORT', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sessions.session_identifier_status_enum AS ENUM ('ACTIVE', 'EXPIRED', 'INVALIDATED', 'SUPERSEDED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sessions.session_lookup_cache_status_enum AS ENUM ('ACTIVE', 'EXPIRED', 'INVALIDATED', 'SUPERSEDED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sessions.session_lookup_type_enum AS ENUM ('PLATE_NUMBER', 'TICKET_NUMBER', 'VENDOR_SESSION_REF', 'QR_REFERENCE', 'COMBINED_PLATE_TICKET');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sessions.session_resolution_channel_enum AS ENUM ('WEB_PAY', 'OPERATOR_ASSISTED', 'INTERNAL_SERVICE', 'RECONCILIATION_RECHECK', 'SUPPORT_RECHECK');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sessions.session_resolution_request_status_enum AS ENUM ('REQUESTED', 'PROCESSING', 'COMPLETED', 'FAILED', 'EXPIRED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sessions.session_resolution_result_status_enum AS ENUM ('RESOLVED_SINGLE', 'NOT_FOUND', 'AMBIGUOUS', 'FAILED', 'EXPIRED', 'CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.device_assignment_status_enum AS ENUM ('ACTIVE', 'SUSPENDED', 'SUPERSEDED', 'EXPIRED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.device_assignment_type_enum AS ENUM ('GATE_DEVICE', 'LPR_DEVICE', 'LANE_CONTROLLER', 'PAYMENT_DEVICE', 'SERVICE_PRINCIPAL', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.lane_direction_enum AS ENUM ('INBOUND', 'OUTBOUND', 'BIDIRECTIONAL', 'NOT_APPLICABLE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.lane_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'MAINTENANCE', 'SUSPENDED', 'INACTIVE', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.lane_type_enum AS ENUM ('ENTRY', 'EXIT', 'MIXED', 'VALIDATION', 'SERVICE', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.site_group_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'INACTIVE', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.site_status_enum AS ENUM ('DRAFT', 'ACTIVE', 'SUSPENDED', 'INACTIVE', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE sites.site_type_enum AS ENUM ('OPEN_LOT', 'STRUCTURED_PARKING', 'MALL_PARKING', 'MIXED_USE_PROPERTY', 'TERMINAL', 'CAMPUS', 'OTHER');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- ============================================================
-- TABLES
-- ============================================================

-- ------------------------------------------------------------
-- core.parking_sessions
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS core.parking_sessions (

    parking_session_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_group_id uuid NOT NULL,
    site_id uuid NOT NULL,
    vendor_system_id uuid NOT NULL,
    vendor_session_ref varchar(128) NOT NULL,
    plate_number_hash char(64),
    plate_number_masked varchar(32),
    ticket_number_hash char(64),
    ticket_number_masked varchar(64),
    entry_at timestamptz,
    vendor_session_status varchar(64),
    session_status core.parking_session_status_enum NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_parking_sessions PRIMARY KEY (parking_session_id)
);
COMMENT ON TABLE core.parking_sessions IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN core.parking_sessions.parking_session_id IS 'Canonical ExitPass identifier for the parking session.';
COMMENT ON COLUMN core.parking_sessions.site_group_id IS 'Site group used for lookup scope and business context.';
COMMENT ON COLUMN core.parking_sessions.site_id IS 'Site where the parking session belongs.';
COMMENT ON COLUMN core.parking_sessions.vendor_system_id IS 'Vendor PMS that owns the raw parking session lifecycle.';
COMMENT ON COLUMN core.parking_sessions.vendor_session_ref IS 'Vendor PMS session reference.';
COMMENT ON COLUMN core.parking_sessions.plate_number_hash IS 'Hash of normalized plate number for lookup and privacy-aware traceability.';
COMMENT ON COLUMN core.parking_sessions.plate_number_masked IS 'Masked or partially redacted plate display value.';
COMMENT ON COLUMN core.parking_sessions.ticket_number_hash IS 'Hash of normalized ticket number where applicable.';
COMMENT ON COLUMN core.parking_sessions.ticket_number_masked IS 'Masked or partially redacted ticket display value.';
COMMENT ON COLUMN core.parking_sessions.entry_at IS 'Entry timestamp as reported by Vendor PMS.';
COMMENT ON COLUMN core.parking_sessions.vendor_session_status IS 'Vendor-reported session status for traceability.';
COMMENT ON COLUMN core.parking_sessions.session_status IS 'ExitPass control status for the canonical session reference.';
COMMENT ON COLUMN core.parking_sessions.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN core.parking_sessions.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN core.parking_sessions.created_by_service_identity_id IS 'Service identity that created the record.';
COMMENT ON COLUMN core.parking_sessions.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN core.parking_sessions.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN core.parking_sessions.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- core.tariff_snapshots
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS core.tariff_snapshots (

    tariff_snapshot_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parking_session_id uuid NOT NULL,
    superseded_by_tariff_snapshot_id uuid,
    vendor_system_id uuid NOT NULL,
    vendor_tariff_ref varchar(128),
    tariff_version_reference varchar(128),
    currency_code char(3) NOT NULL,
    gross_amount numeric(18,2) NOT NULL,
    statutory_discount_amount numeric(18,2) NOT NULL,
    coupon_discount_amount numeric(18,2) NOT NULL,
    net_amount numeric(18,2) NOT NULL,
    statutory_discount_validation_id uuid,
    coupon_application_id uuid,
    snapshot_status core.tariff_snapshot_status_enum NOT NULL,
    calculated_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_tariff_snapshots PRIMARY KEY (tariff_snapshot_id)
);
COMMENT ON TABLE core.tariff_snapshots IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN core.tariff_snapshots.tariff_snapshot_id IS 'Canonical identifier of the tariff snapshot.';
COMMENT ON COLUMN core.tariff_snapshots.parking_session_id IS 'Parking session for which this payable basis was created.';
COMMENT ON COLUMN core.tariff_snapshots.superseded_by_tariff_snapshot_id IS 'Later snapshot that superseded this snapshot.';
COMMENT ON COLUMN core.tariff_snapshots.vendor_system_id IS 'Vendor PMS that supplied the tariff basis.';
COMMENT ON COLUMN core.tariff_snapshots.vendor_tariff_ref IS 'Vendor tariff calculation reference, where available.';
COMMENT ON COLUMN core.tariff_snapshots.tariff_version_reference IS 'Vendor or configured tariff version reference used for traceability.';
COMMENT ON COLUMN core.tariff_snapshots.currency_code IS 'Currency code.';
COMMENT ON COLUMN core.tariff_snapshots.gross_amount IS 'Vendor-authoritative amount before discounts and coupons.';
COMMENT ON COLUMN core.tariff_snapshots.statutory_discount_amount IS 'Total statutory discount amount included in the snapshot.';
COMMENT ON COLUMN core.tariff_snapshots.coupon_discount_amount IS 'Total coupon amount included in the snapshot.';
COMMENT ON COLUMN core.tariff_snapshots.net_amount IS 'Final payable amount used for payment.';
COMMENT ON COLUMN core.tariff_snapshots.statutory_discount_validation_id IS 'Approved statutory validation reflected in the snapshot, if any.';
COMMENT ON COLUMN core.tariff_snapshots.coupon_application_id IS 'Coupon application reflected in the snapshot, if any.';
COMMENT ON COLUMN core.tariff_snapshots.snapshot_status IS 'Current lifecycle state of the snapshot.';
COMMENT ON COLUMN core.tariff_snapshots.calculated_at IS 'Timestamp when payable basis was calculated or accepted.';
COMMENT ON COLUMN core.tariff_snapshots.expires_at IS 'Timestamp after which the snapshot may no longer create a payment attempt.';
COMMENT ON COLUMN core.tariff_snapshots.consumed_at IS 'Timestamp when snapshot became bound to a payment attempt.';
COMMENT ON COLUMN core.tariff_snapshots.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN core.tariff_snapshots.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN core.tariff_snapshots.created_by_service_identity_id IS 'Service identity that created the snapshot.';
COMMENT ON COLUMN core.tariff_snapshots.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN core.tariff_snapshots.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN core.tariff_snapshots.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- core.payment_attempts
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS core.payment_attempts (

    payment_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parking_session_id uuid NOT NULL,
    tariff_snapshot_id uuid NOT NULL,
    idempotency_key varchar(128) NOT NULL,
    payment_rail_id uuid,
    currency_code char(3) NOT NULL,
    amount numeric(18,2) NOT NULL,
    attempt_status core.payment_attempt_status_enum NOT NULL,
    requested_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    finalized_at timestamptz,
    failure_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_payment_attempts PRIMARY KEY (payment_attempt_id)
);
COMMENT ON TABLE core.payment_attempts IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN core.payment_attempts.payment_attempt_id IS 'Canonical identifier of the payment attempt.';
COMMENT ON COLUMN core.payment_attempts.parking_session_id IS 'Parking session being paid for.';
COMMENT ON COLUMN core.payment_attempts.tariff_snapshot_id IS 'Immutable payable basis used by this attempt.';
COMMENT ON COLUMN core.payment_attempts.idempotency_key IS 'Client or service-supplied idempotency key.';
COMMENT ON COLUMN core.payment_attempts.payment_rail_id IS 'Intended or selected payment rail.';
COMMENT ON COLUMN core.payment_attempts.currency_code IS 'Currency code.';
COMMENT ON COLUMN core.payment_attempts.amount IS 'Amount to be paid, copied from bound tariff snapshot.';
COMMENT ON COLUMN core.payment_attempts.attempt_status IS 'Lifecycle state of the payment attempt.';
COMMENT ON COLUMN core.payment_attempts.requested_at IS 'Timestamp when the attempt was requested.';
COMMENT ON COLUMN core.payment_attempts.expires_at IS 'Expiry boundary for the payment attempt.';
COMMENT ON COLUMN core.payment_attempts.finalized_at IS 'Timestamp when attempt reached terminal finality.';
COMMENT ON COLUMN core.payment_attempts.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN core.payment_attempts.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN core.payment_attempts.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN core.payment_attempts.created_by_service_identity_id IS 'Service identity that created the attempt.';
COMMENT ON COLUMN core.payment_attempts.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN core.payment_attempts.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN core.payment_attempts.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- core.payment_confirmations
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS core.payment_confirmations (

    payment_confirmation_id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_attempt_id uuid NOT NULL,
    provider_outcome_id uuid,
    payment_rail_id uuid,
    provider_transaction_ref varchar(128),
    currency_code char(3) NOT NULL,
    confirmed_amount numeric(18,2) NOT NULL,
    confirmation_status core.payment_confirmation_status_enum NOT NULL,
    verified_at timestamptz NOT NULL,
    confirmed_at timestamptz NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    CONSTRAINT pk_payment_confirmations PRIMARY KEY (payment_confirmation_id)
);
COMMENT ON TABLE core.payment_confirmations IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN core.payment_confirmations.payment_confirmation_id IS 'Canonical identifier of the payment confirmation.';
COMMENT ON COLUMN core.payment_confirmations.payment_attempt_id IS 'Payment attempt confirmed by this record.';
COMMENT ON COLUMN core.payment_confirmations.provider_outcome_id IS 'Verified provider outcome that supported confirmation.';
COMMENT ON COLUMN core.payment_confirmations.payment_rail_id IS 'Rail through which the payment was completed.';
COMMENT ON COLUMN core.payment_confirmations.provider_transaction_ref IS 'Provider transaction reference used for traceability.';
COMMENT ON COLUMN core.payment_confirmations.currency_code IS 'Currency code.';
COMMENT ON COLUMN core.payment_confirmations.confirmed_amount IS 'Amount confirmed as paid.';
COMMENT ON COLUMN core.payment_confirmations.confirmation_status IS 'Confirmation record state.';
COMMENT ON COLUMN core.payment_confirmations.verified_at IS 'Timestamp when provider evidence was verified.';
COMMENT ON COLUMN core.payment_confirmations.confirmed_at IS 'Timestamp when Central PMS recorded canonical confirmation.';
COMMENT ON COLUMN core.payment_confirmations.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN core.payment_confirmations.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN core.payment_confirmations.created_by_service_identity_id IS 'Service identity that created confirmation.';

-- ------------------------------------------------------------
-- core.exit_authorizations
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS core.exit_authorizations (

    exit_authorization_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parking_session_id uuid NOT NULL,
    payment_attempt_id uuid NOT NULL,
    payment_confirmation_id uuid NOT NULL,
    authorization_token_hash char(64) NOT NULL,
    authorization_status core.exit_authorization_status_enum NOT NULL,
    issued_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    invalidated_at timestamptz,
    invalidation_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_exit_authorizations PRIMARY KEY (exit_authorization_id)
);
COMMENT ON TABLE core.exit_authorizations IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN core.exit_authorizations.exit_authorization_id IS 'Canonical identifier of the exit authorization.';
COMMENT ON COLUMN core.exit_authorizations.parking_session_id IS 'Parking session for which exit is authorized.';
COMMENT ON COLUMN core.exit_authorizations.payment_attempt_id IS 'Confirmed payment attempt that established financial control state.';
COMMENT ON COLUMN core.exit_authorizations.payment_confirmation_id IS 'Canonical payment confirmation supporting issuance.';
COMMENT ON COLUMN core.exit_authorizations.authorization_token_hash IS 'Hash of opaque token used for secure lookup and replay-safe validation.';
COMMENT ON COLUMN core.exit_authorizations.authorization_status IS 'Current validity status of the authorization.';
COMMENT ON COLUMN core.exit_authorizations.issued_at IS 'Issuance timestamp.';
COMMENT ON COLUMN core.exit_authorizations.expires_at IS 'Expiration boundary for authorization validity.';
COMMENT ON COLUMN core.exit_authorizations.invalidated_at IS 'Timestamp when authorization was invalidated, if applicable.';
COMMENT ON COLUMN core.exit_authorizations.invalidation_reason_code IS 'Controlled reason for invalidation.';
COMMENT ON COLUMN core.exit_authorizations.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN core.exit_authorizations.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN core.exit_authorizations.created_by_service_identity_id IS 'Creating service identity.';
COMMENT ON COLUMN core.exit_authorizations.updated_at IS 'Last mutation timestamp.';
COMMENT ON COLUMN core.exit_authorizations.updated_by_service_identity_id IS 'Updating service identity.';
COMMENT ON COLUMN core.exit_authorizations.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- payments.payment_rails
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payments.payment_rails (

    payment_rail_id uuid DEFAULT gen_random_uuid() NOT NULL,
    rail_code varchar(64) NOT NULL,
    rail_name varchar(128) NOT NULL,
    provider_code varchar(64) NOT NULL,
    rail_type payments.payment_rail_type_enum NOT NULL,
    supported_currency_code char(3) NOT NULL,
    rail_status payments.payment_rail_status_enum NOT NULL,
    is_primary boolean DEFAULT false NOT NULL,
    is_fallback boolean DEFAULT false NOT NULL,
    provider_profile_ref varchar(128),
    configuration_ref varchar(128),
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_payment_rails PRIMARY KEY (payment_rail_id)
);
COMMENT ON TABLE payments.payment_rails IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN payments.payment_rails.payment_rail_id IS 'Canonical identifier of the payment rail.';
COMMENT ON COLUMN payments.payment_rails.rail_code IS 'Stable internal code for the rail.';
COMMENT ON COLUMN payments.payment_rails.rail_name IS 'Human-readable payment rail name.';
COMMENT ON COLUMN payments.payment_rails.provider_code IS 'Provider code.';
COMMENT ON COLUMN payments.payment_rails.rail_type IS 'Type of rail.';
COMMENT ON COLUMN payments.payment_rails.supported_currency_code IS 'Supported currency.';
COMMENT ON COLUMN payments.payment_rails.rail_status IS 'Lifecycle status of the payment rail.';
COMMENT ON COLUMN payments.payment_rails.is_primary IS 'Indicates whether this is the preferred rail for its type.';
COMMENT ON COLUMN payments.payment_rails.is_fallback IS 'Indicates whether this rail may be used as fallback.';
COMMENT ON COLUMN payments.payment_rails.provider_profile_ref IS 'External or internal provider profile reference.';
COMMENT ON COLUMN payments.payment_rails.configuration_ref IS 'Configuration profile reference.';
COMMENT ON COLUMN payments.payment_rails.effective_from IS 'Start of rail effectiveness.';
COMMENT ON COLUMN payments.payment_rails.effective_to IS 'End of rail effectiveness.';
COMMENT ON COLUMN payments.payment_rails.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN payments.payment_rails.created_by_user_id IS 'User who created the rail record.';
COMMENT ON COLUMN payments.payment_rails.created_by_service_identity_id IS 'Service identity that created the rail record.';
COMMENT ON COLUMN payments.payment_rails.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN payments.payment_rails.updated_by_user_id IS 'User who last updated the rail record.';
COMMENT ON COLUMN payments.payment_rails.updated_by_service_identity_id IS 'Service identity that last updated the rail record.';
COMMENT ON COLUMN payments.payment_rails.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- payments.provider_sessions
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payments.provider_sessions (

    provider_session_id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_attempt_id uuid NOT NULL,
    payment_rail_id uuid NOT NULL,
    provider_session_ref varchar(128),
    provider_transaction_ref varchar(128),
    idempotency_key varchar(128) NOT NULL,
    session_status payments.provider_session_status_enum NOT NULL,
    currency_code char(3) NOT NULL,
    amount numeric(18,2) NOT NULL,
    checkout_url text,
    qr_payload text,
    expires_at timestamptz,
    provider_created_at timestamptz,
    provider_expires_at timestamptz,
    raw_provider_metadata_ref varchar(128),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_provider_sessions PRIMARY KEY (provider_session_id)
);
COMMENT ON TABLE payments.provider_sessions IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN payments.provider_sessions.provider_session_id IS 'Canonical identifier of the provider session.';
COMMENT ON COLUMN payments.provider_sessions.payment_attempt_id IS 'Target payment attempt.';
COMMENT ON COLUMN payments.provider_sessions.payment_rail_id IS 'Payment rail used for provider execution.';
COMMENT ON COLUMN payments.provider_sessions.provider_session_ref IS 'Provider-side session, checkout, intent, or order reference.';
COMMENT ON COLUMN payments.provider_sessions.provider_transaction_ref IS 'Provider transaction reference where known at session creation.';
COMMENT ON COLUMN payments.provider_sessions.idempotency_key IS 'Idempotency key for provider-session creation.';
COMMENT ON COLUMN payments.provider_sessions.session_status IS 'Provider session lifecycle state.';
COMMENT ON COLUMN payments.provider_sessions.currency_code IS 'Currency code.';
COMMENT ON COLUMN payments.provider_sessions.amount IS 'Amount submitted to provider.';
COMMENT ON COLUMN payments.provider_sessions.checkout_url IS 'Hosted checkout URL where applicable.';
COMMENT ON COLUMN payments.provider_sessions.qr_payload IS 'QR or QRPh payload where applicable.';
COMMENT ON COLUMN payments.provider_sessions.expires_at IS 'Provider session expiry timestamp.';
COMMENT ON COLUMN payments.provider_sessions.provider_created_at IS 'Provider-side creation timestamp where known.';
COMMENT ON COLUMN payments.provider_sessions.provider_expires_at IS 'Provider-side expiry timestamp where known.';
COMMENT ON COLUMN payments.provider_sessions.raw_provider_metadata_ref IS 'Reference to stored provider metadata if retained separately.';
COMMENT ON COLUMN payments.provider_sessions.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN payments.provider_sessions.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN payments.provider_sessions.created_by_service_identity_id IS 'Service identity that created the provider session.';
COMMENT ON COLUMN payments.provider_sessions.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN payments.provider_sessions.updated_by_service_identity_id IS 'Service identity that last updated the provider session.';
COMMENT ON COLUMN payments.provider_sessions.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- payments.provider_callbacks
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payments.provider_callbacks (

    provider_callback_id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_rail_id uuid NOT NULL,
    provider_session_id uuid,
    payment_attempt_id uuid,
    provider_event_ref varchar(128),
    provider_transaction_ref varchar(128),
    callback_type varchar(64) NOT NULL,
    payload_hash char(64) NOT NULL,
    payload_storage_ref varchar(256),
    headers_hash char(64),
    signature_valid boolean,
    timestamp_valid boolean,
    source_valid boolean,
    verification_status payments.provider_callback_verification_status_enum NOT NULL,
    processing_status payments.provider_callback_processing_status_enum NOT NULL,
    received_at timestamptz NOT NULL,
    processed_at timestamptz,
    failure_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    CONSTRAINT pk_provider_callbacks PRIMARY KEY (provider_callback_id)
);
COMMENT ON TABLE payments.provider_callbacks IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN payments.provider_callbacks.provider_callback_id IS 'Canonical identifier of the provider callback record.';
COMMENT ON COLUMN payments.provider_callbacks.payment_rail_id IS 'Payment rail or provider profile that received the callback.';
COMMENT ON COLUMN payments.provider_callbacks.provider_session_id IS 'Provider session correlated to the callback, where known.';
COMMENT ON COLUMN payments.provider_callbacks.payment_attempt_id IS 'Payment attempt correlated to the callback, where known.';
COMMENT ON COLUMN payments.provider_callbacks.provider_event_ref IS 'Provider event ID or callback reference.';
COMMENT ON COLUMN payments.provider_callbacks.provider_transaction_ref IS 'Provider transaction reference in the callback.';
COMMENT ON COLUMN payments.provider_callbacks.callback_type IS 'Provider callback type or normalized event type.';
COMMENT ON COLUMN payments.provider_callbacks.payload_hash IS 'SHA-256 hash of raw callback payload.';
COMMENT ON COLUMN payments.provider_callbacks.payload_storage_ref IS 'Reference to raw payload storage if stored outside table.';
COMMENT ON COLUMN payments.provider_callbacks.headers_hash IS 'Hash of relevant callback headers where retained.';
COMMENT ON COLUMN payments.provider_callbacks.signature_valid IS 'Result of signature verification where applicable.';
COMMENT ON COLUMN payments.provider_callbacks.timestamp_valid IS 'Result of timestamp-window verification where applicable.';
COMMENT ON COLUMN payments.provider_callbacks.source_valid IS 'Result of source validation where applicable.';
COMMENT ON COLUMN payments.provider_callbacks.verification_status IS 'Trust verification result.';
COMMENT ON COLUMN payments.provider_callbacks.processing_status IS 'Processing lifecycle state.';
COMMENT ON COLUMN payments.provider_callbacks.received_at IS 'Timestamp when callback was received.';
COMMENT ON COLUMN payments.provider_callbacks.processed_at IS 'Timestamp when callback processing completed.';
COMMENT ON COLUMN payments.provider_callbacks.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN payments.provider_callbacks.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN payments.provider_callbacks.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN payments.provider_callbacks.created_by_service_identity_id IS 'Receiving service identity.';

-- ------------------------------------------------------------
-- payments.provider_status_queries
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payments.provider_status_queries (

    provider_status_query_id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_attempt_id uuid NOT NULL,
    provider_session_id uuid,
    payment_rail_id uuid NOT NULL,
    provider_transaction_ref varchar(128),
    query_status payments.provider_status_query_status_enum NOT NULL,
    provider_result_status varchar(64),
    http_status_code integer,
    request_hash char(64),
    response_hash char(64),
    response_storage_ref varchar(256),
    failure_reason_code varchar(64),
    requested_at timestamptz NOT NULL,
    completed_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    CONSTRAINT pk_provider_status_queries PRIMARY KEY (provider_status_query_id)
);
COMMENT ON TABLE payments.provider_status_queries IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN payments.provider_status_queries.provider_status_query_id IS 'Canonical identifier of the provider status query.';
COMMENT ON COLUMN payments.provider_status_queries.payment_attempt_id IS 'Payment attempt being investigated or verified.';
COMMENT ON COLUMN payments.provider_status_queries.provider_session_id IS 'Provider session being queried.';
COMMENT ON COLUMN payments.provider_status_queries.payment_rail_id IS 'Payment rail queried.';
COMMENT ON COLUMN payments.provider_status_queries.provider_transaction_ref IS 'Provider transaction reference used for query.';
COMMENT ON COLUMN payments.provider_status_queries.query_status IS 'Query lifecycle state.';
COMMENT ON COLUMN payments.provider_status_queries.provider_result_status IS 'Raw or provider-normalized result status.';
COMMENT ON COLUMN payments.provider_status_queries.http_status_code IS 'HTTP status returned by provider.';
COMMENT ON COLUMN payments.provider_status_queries.request_hash IS 'Hash of request payload or request signature basis.';
COMMENT ON COLUMN payments.provider_status_queries.response_hash IS 'Hash of provider response payload where retained.';
COMMENT ON COLUMN payments.provider_status_queries.response_storage_ref IS 'Reference to response payload if stored externally.';
COMMENT ON COLUMN payments.provider_status_queries.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN payments.provider_status_queries.requested_at IS 'Query request timestamp.';
COMMENT ON COLUMN payments.provider_status_queries.completed_at IS 'Query completion timestamp.';
COMMENT ON COLUMN payments.provider_status_queries.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN payments.provider_status_queries.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN payments.provider_status_queries.created_by_service_identity_id IS 'Querying service identity.';

-- ------------------------------------------------------------
-- payments.provider_outcomes
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payments.provider_outcomes (

    provider_outcome_id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_attempt_id uuid NOT NULL,
    provider_session_id uuid,
    provider_callback_id uuid,
    provider_status_query_id uuid,
    payment_rail_id uuid NOT NULL,
    provider_transaction_ref varchar(128),
    provider_outcome_status payments.provider_outcome_status_enum NOT NULL,
    provider_native_status varchar(64),
    currency_code char(3) NOT NULL,
    amount numeric(18,2) NOT NULL,
    verified_at timestamptz NOT NULL,
    reported_to_central_pms_at timestamptz,
    central_pms_report_status payments.central_pms_report_status_enum NOT NULL,
    failure_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_provider_outcomes PRIMARY KEY (provider_outcome_id)
);
COMMENT ON TABLE payments.provider_outcomes IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN payments.provider_outcomes.provider_outcome_id IS 'Canonical identifier of the provider outcome.';
COMMENT ON COLUMN payments.provider_outcomes.payment_attempt_id IS 'Payment attempt that the outcome relates to.';
COMMENT ON COLUMN payments.provider_outcomes.provider_session_id IS 'Provider session that produced the outcome.';
COMMENT ON COLUMN payments.provider_outcomes.provider_callback_id IS 'Callback that supported the outcome, if applicable.';
COMMENT ON COLUMN payments.provider_outcomes.provider_status_query_id IS 'Status query that supported the outcome, if applicable.';
COMMENT ON COLUMN payments.provider_outcomes.payment_rail_id IS 'Payment rail that produced the outcome.';
COMMENT ON COLUMN payments.provider_outcomes.provider_transaction_ref IS 'Provider transaction reference.';
COMMENT ON COLUMN payments.provider_outcomes.provider_outcome_status IS 'Canonicalized provider-side result.';
COMMENT ON COLUMN payments.provider_outcomes.provider_native_status IS 'Native provider status value.';
COMMENT ON COLUMN payments.provider_outcomes.currency_code IS 'Currency code.';
COMMENT ON COLUMN payments.provider_outcomes.amount IS 'Amount verified from provider evidence.';
COMMENT ON COLUMN payments.provider_outcomes.verified_at IS 'Timestamp when evidence was verified.';
COMMENT ON COLUMN payments.provider_outcomes.reported_to_central_pms_at IS 'Timestamp when verified outcome was reported to Central PMS.';
COMMENT ON COLUMN payments.provider_outcomes.central_pms_report_status IS 'Report-to-Central-PMS lifecycle state.';
COMMENT ON COLUMN payments.provider_outcomes.failure_reason_code IS 'Controlled failure or rejection reason.';
COMMENT ON COLUMN payments.provider_outcomes.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN payments.provider_outcomes.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN payments.provider_outcomes.created_by_service_identity_id IS 'Service identity that created the outcome.';
COMMENT ON COLUMN payments.provider_outcomes.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN payments.provider_outcomes.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN payments.provider_outcomes.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- sessions.session_resolution_requests
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sessions.session_resolution_requests (

    session_resolution_request_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_group_id uuid NOT NULL,
    site_id uuid,
    lookup_type sessions.session_lookup_type_enum NOT NULL,
    lookup_identifier_hash char(64) NOT NULL,
    lookup_identifier_masked varchar(64),
    request_channel sessions.session_resolution_channel_enum NOT NULL,
    request_status sessions.session_resolution_request_status_enum NOT NULL,
    client_reference varchar(128),
    idempotency_key varchar(128),
    rate_limit_key_hash char(64),
    requested_at timestamptz NOT NULL,
    expires_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_session_resolution_requests PRIMARY KEY (session_resolution_request_id)
);
COMMENT ON TABLE sessions.session_resolution_requests IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sessions.session_resolution_requests.session_resolution_request_id IS 'Canonical identifier of the lookup request.';
COMMENT ON COLUMN sessions.session_resolution_requests.site_group_id IS 'Site group scope used for lookup.';
COMMENT ON COLUMN sessions.session_resolution_requests.site_id IS 'Specific site scope, if already known.';
COMMENT ON COLUMN sessions.session_resolution_requests.lookup_type IS 'Type of lookup submitted.';
COMMENT ON COLUMN sessions.session_resolution_requests.lookup_identifier_hash IS 'Hash of normalized lookup identifier.';
COMMENT ON COLUMN sessions.session_resolution_requests.lookup_identifier_masked IS 'Masked display value of submitted identifier.';
COMMENT ON COLUMN sessions.session_resolution_requests.request_channel IS 'Channel where request originated.';
COMMENT ON COLUMN sessions.session_resolution_requests.request_status IS 'Request lifecycle state.';
COMMENT ON COLUMN sessions.session_resolution_requests.client_reference IS 'Optional client-side or UI flow reference.';
COMMENT ON COLUMN sessions.session_resolution_requests.idempotency_key IS 'Idempotency key for repeated lookup request, where used.';
COMMENT ON COLUMN sessions.session_resolution_requests.rate_limit_key_hash IS 'Hashed rate-limit key where lookup throttling applies.';
COMMENT ON COLUMN sessions.session_resolution_requests.requested_at IS 'Lookup request timestamp.';
COMMENT ON COLUMN sessions.session_resolution_requests.expires_at IS 'Request expiry timestamp where lookup has a bounded validity window.';
COMMENT ON COLUMN sessions.session_resolution_requests.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN sessions.session_resolution_requests.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sessions.session_resolution_requests.created_by_user_id IS 'Human user who created the request, if operator-assisted.';
COMMENT ON COLUMN sessions.session_resolution_requests.created_by_service_identity_id IS 'Service identity that created the request.';
COMMENT ON COLUMN sessions.session_resolution_requests.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- sessions.session_resolution_results
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sessions.session_resolution_results (

    session_resolution_result_id uuid DEFAULT gen_random_uuid() NOT NULL,
    session_resolution_request_id uuid NOT NULL,
    parking_session_id uuid,
    site_group_id uuid NOT NULL,
    site_id uuid,
    vendor_system_id uuid,
    vendor_session_ref varchar(128),
    result_status sessions.session_resolution_result_status_enum NOT NULL,
    match_count integer NOT NULL,
    ambiguity_reason_code varchar(64),
    failure_reason_code varchar(64),
    resolved_at timestamptz NOT NULL,
    expires_at timestamptz,
    raw_result_ref varchar(256),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    CONSTRAINT pk_session_resolution_results PRIMARY KEY (session_resolution_result_id)
);
COMMENT ON TABLE sessions.session_resolution_results IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sessions.session_resolution_results.session_resolution_result_id IS 'Canonical identifier of the lookup result.';
COMMENT ON COLUMN sessions.session_resolution_results.session_resolution_request_id IS 'Lookup request that produced this result.';
COMMENT ON COLUMN sessions.session_resolution_results.parking_session_id IS 'Canonical parking session created or reused after deterministic match.';
COMMENT ON COLUMN sessions.session_resolution_results.site_group_id IS 'Site group scope used for lookup.';
COMMENT ON COLUMN sessions.session_resolution_results.site_id IS 'Resolved site, where known.';
COMMENT ON COLUMN sessions.session_resolution_results.vendor_system_id IS 'Vendor PMS that produced the lookup result.';
COMMENT ON COLUMN sessions.session_resolution_results.vendor_session_ref IS 'Vendor PMS session reference if resolved.';
COMMENT ON COLUMN sessions.session_resolution_results.result_status IS 'Lookup outcome.';
COMMENT ON COLUMN sessions.session_resolution_results.match_count IS 'Number of matches returned or determined.';
COMMENT ON COLUMN sessions.session_resolution_results.ambiguity_reason_code IS 'Controlled reason when result is ambiguous.';
COMMENT ON COLUMN sessions.session_resolution_results.failure_reason_code IS 'Controlled reason when lookup failed.';
COMMENT ON COLUMN sessions.session_resolution_results.resolved_at IS 'Timestamp when result was resolved.';
COMMENT ON COLUMN sessions.session_resolution_results.expires_at IS 'Expiry of result usability.';
COMMENT ON COLUMN sessions.session_resolution_results.raw_result_ref IS 'Reference to raw vendor lookup result where retained.';
COMMENT ON COLUMN sessions.session_resolution_results.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN sessions.session_resolution_results.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sessions.session_resolution_results.created_by_service_identity_id IS 'Service identity that created the result.';

-- ------------------------------------------------------------
-- sessions.session_lookup_cache
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sessions.session_lookup_cache (

    session_lookup_cache_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_group_id uuid NOT NULL,
    site_id uuid,
    parking_session_id uuid,
    vendor_system_id uuid,
    lookup_type sessions.session_lookup_type_enum NOT NULL,
    lookup_identifier_hash char(64) NOT NULL,
    result_status sessions.session_resolution_result_status_enum NOT NULL,
    cache_status sessions.session_lookup_cache_status_enum NOT NULL,
    cached_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    invalidated_at timestamptz,
    invalidation_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_session_lookup_cache PRIMARY KEY (session_lookup_cache_id)
);
COMMENT ON TABLE sessions.session_lookup_cache IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sessions.session_lookup_cache.session_lookup_cache_id IS 'Canonical identifier of the cache entry.';
COMMENT ON COLUMN sessions.session_lookup_cache.site_group_id IS 'Site group scope of the cached lookup.';
COMMENT ON COLUMN sessions.session_lookup_cache.site_id IS 'Site context where known.';
COMMENT ON COLUMN sessions.session_lookup_cache.parking_session_id IS 'Canonical parking session if lookup was resolved.';
COMMENT ON COLUMN sessions.session_lookup_cache.vendor_system_id IS 'Vendor PMS that produced the cached result.';
COMMENT ON COLUMN sessions.session_lookup_cache.lookup_type IS 'Lookup identifier type.';
COMMENT ON COLUMN sessions.session_lookup_cache.lookup_identifier_hash IS 'Hash of normalized lookup identifier.';
COMMENT ON COLUMN sessions.session_lookup_cache.result_status IS 'Cached result status.';
COMMENT ON COLUMN sessions.session_lookup_cache.cache_status IS 'Cache entry lifecycle state.';
COMMENT ON COLUMN sessions.session_lookup_cache.cached_at IS 'Timestamp when cache entry was created.';
COMMENT ON COLUMN sessions.session_lookup_cache.expires_at IS 'Cache entry expiry timestamp.';
COMMENT ON COLUMN sessions.session_lookup_cache.invalidated_at IS 'Timestamp when cache entry was invalidated.';
COMMENT ON COLUMN sessions.session_lookup_cache.invalidation_reason_code IS 'Controlled invalidation reason.';
COMMENT ON COLUMN sessions.session_lookup_cache.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN sessions.session_lookup_cache.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sessions.session_lookup_cache.created_by_service_identity_id IS 'Service identity that created the cache entry.';
COMMENT ON COLUMN sessions.session_lookup_cache.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- sessions.session_identifier_indexes
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sessions.session_identifier_indexes (

    session_identifier_index_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parking_session_id uuid,
    site_group_id uuid NOT NULL,
    site_id uuid,
    vendor_system_id uuid,
    identifier_type sessions.session_lookup_type_enum NOT NULL,
    identifier_hash char(64) NOT NULL,
    identifier_masked varchar(64),
    identifier_status sessions.session_identifier_status_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    correlation_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_session_identifier_indexes PRIMARY KEY (session_identifier_index_id)
);
COMMENT ON TABLE sessions.session_identifier_indexes IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sessions.session_identifier_indexes.session_identifier_index_id IS 'Canonical identifier of the identifier index record.';
COMMENT ON COLUMN sessions.session_identifier_indexes.parking_session_id IS 'Canonical parking session associated with the identifier.';
COMMENT ON COLUMN sessions.session_identifier_indexes.site_group_id IS 'Site group scope for identifier lookup.';
COMMENT ON COLUMN sessions.session_identifier_indexes.site_id IS 'Site context where known.';
COMMENT ON COLUMN sessions.session_identifier_indexes.vendor_system_id IS 'Vendor system context where identifier is vendor-originated.';
COMMENT ON COLUMN sessions.session_identifier_indexes.identifier_type IS 'Identifier type.';
COMMENT ON COLUMN sessions.session_identifier_indexes.identifier_hash IS 'Hash of normalized identifier.';
COMMENT ON COLUMN sessions.session_identifier_indexes.identifier_masked IS 'Masked display value.';
COMMENT ON COLUMN sessions.session_identifier_indexes.identifier_status IS 'Identifier lifecycle state.';
COMMENT ON COLUMN sessions.session_identifier_indexes.effective_from IS 'Start of identifier validity.';
COMMENT ON COLUMN sessions.session_identifier_indexes.effective_to IS 'End of identifier validity.';
COMMENT ON COLUMN sessions.session_identifier_indexes.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sessions.session_identifier_indexes.created_by_service_identity_id IS 'Service identity that created the identifier index.';
COMMENT ON COLUMN sessions.session_identifier_indexes.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN sessions.session_identifier_indexes.updated_by_service_identity_id IS 'Service identity that last updated the identifier index.';
COMMENT ON COLUMN sessions.session_identifier_indexes.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN sessions.session_identifier_indexes.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- coupons.coupons
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS coupons.coupons (

    coupon_id uuid DEFAULT gen_random_uuid() NOT NULL,
    merchant_id uuid NOT NULL,
    coupon_code varchar(64) NOT NULL,
    coupon_name varchar(128) NOT NULL,
    coupon_description text,
    coupon_type coupons.coupon_type_enum NOT NULL,
    denomination_type coupons.coupon_denomination_type_enum NOT NULL,
    denomination_value numeric(18,2) NOT NULL,
    currency_code char(3),
    maximum_discount_amount numeric(18,2),
    minimum_gross_amount numeric(18,2),
    stacking_policy coupons.coupon_stacking_policy_enum NOT NULL,
    allows_full_waiver boolean DEFAULT false NOT NULL,
    requires_elevated_approval boolean DEFAULT false NOT NULL,
    coupon_status coupons.coupon_status_enum NOT NULL,
    valid_from timestamptz NOT NULL,
    valid_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_coupons PRIMARY KEY (coupon_id)
);
COMMENT ON TABLE coupons.coupons IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN coupons.coupons.coupon_id IS 'Canonical identifier of the coupon.';
COMMENT ON COLUMN coupons.coupons.merchant_id IS 'Merchant sponsor of the coupon.';
COMMENT ON COLUMN coupons.coupons.coupon_code IS 'Stable coupon code.';
COMMENT ON COLUMN coupons.coupons.coupon_name IS 'Human-readable coupon name.';
COMMENT ON COLUMN coupons.coupons.coupon_description IS 'Description of the coupon program.';
COMMENT ON COLUMN coupons.coupons.coupon_type IS 'Coupon type or commercial category.';
COMMENT ON COLUMN coupons.coupons.denomination_type IS 'Discount denomination type.';
COMMENT ON COLUMN coupons.coupons.denomination_value IS 'Fixed amount, percentage, or controlled value depending on denomination type.';
COMMENT ON COLUMN coupons.coupons.currency_code IS 'Currency code for fixed-amount coupons.';
COMMENT ON COLUMN coupons.coupons.maximum_discount_amount IS 'Maximum discount amount where capped.';
COMMENT ON COLUMN coupons.coupons.minimum_gross_amount IS 'Minimum gross amount required for coupon use.';
COMMENT ON COLUMN coupons.coupons.stacking_policy IS 'Stacking behavior with other coupons or statutory discounts.';
COMMENT ON COLUMN coupons.coupons.allows_full_waiver IS 'Indicates whether the coupon may waive the full payable amount.';
COMMENT ON COLUMN coupons.coupons.requires_elevated_approval IS 'Indicates whether coupon use requires elevated approval.';
COMMENT ON COLUMN coupons.coupons.coupon_status IS 'Coupon lifecycle status.';
COMMENT ON COLUMN coupons.coupons.valid_from IS 'Start of coupon validity.';
COMMENT ON COLUMN coupons.coupons.valid_to IS 'End of coupon validity.';
COMMENT ON COLUMN coupons.coupons.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN coupons.coupons.created_by_user_id IS 'User who created the coupon.';
COMMENT ON COLUMN coupons.coupons.created_by_service_identity_id IS 'Service identity that created the coupon.';
COMMENT ON COLUMN coupons.coupons.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN coupons.coupons.updated_by_user_id IS 'User who last updated the coupon.';
COMMENT ON COLUMN coupons.coupons.updated_by_service_identity_id IS 'Service identity that last updated the coupon.';
COMMENT ON COLUMN coupons.coupons.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- coupons.coupon_rule_groups
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS coupons.coupon_rule_groups (

    coupon_rule_group_id uuid DEFAULT gen_random_uuid() NOT NULL,
    coupon_id uuid NOT NULL,
    rule_group_code varchar(64) NOT NULL,
    rule_group_name varchar(128) NOT NULL,
    rule_group_description text,
    evaluation_strategy coupons.coupon_rule_evaluation_strategy_enum NOT NULL,
    evaluation_priority integer NOT NULL,
    is_required boolean DEFAULT false NOT NULL,
    rule_group_status coupons.coupon_rule_group_status_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_coupon_rule_groups PRIMARY KEY (coupon_rule_group_id)
);
COMMENT ON TABLE coupons.coupon_rule_groups IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN coupons.coupon_rule_groups.coupon_rule_group_id IS 'Canonical identifier of the coupon rule group.';
COMMENT ON COLUMN coupons.coupon_rule_groups.coupon_id IS 'Coupon to which the rule group belongs.';
COMMENT ON COLUMN coupons.coupon_rule_groups.rule_group_code IS 'Stable code for the rule group.';
COMMENT ON COLUMN coupons.coupon_rule_groups.rule_group_name IS 'Human-readable name.';
COMMENT ON COLUMN coupons.coupon_rule_groups.rule_group_description IS 'Description of the rule group.';
COMMENT ON COLUMN coupons.coupon_rule_groups.evaluation_strategy IS 'Rule group evaluation strategy.';
COMMENT ON COLUMN coupons.coupon_rule_groups.evaluation_priority IS 'Evaluation priority when multiple groups exist.';
COMMENT ON COLUMN coupons.coupon_rule_groups.is_required IS 'Indicates whether the group must pass for eligibility.';
COMMENT ON COLUMN coupons.coupon_rule_groups.rule_group_status IS 'Rule group lifecycle status.';
COMMENT ON COLUMN coupons.coupon_rule_groups.effective_from IS 'Start of rule group effectiveness.';
COMMENT ON COLUMN coupons.coupon_rule_groups.effective_to IS 'End of rule group effectiveness.';
COMMENT ON COLUMN coupons.coupon_rule_groups.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN coupons.coupon_rule_groups.created_by_user_id IS 'User who created the rule group.';
COMMENT ON COLUMN coupons.coupon_rule_groups.created_by_service_identity_id IS 'Service identity that created the rule group.';
COMMENT ON COLUMN coupons.coupon_rule_groups.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN coupons.coupon_rule_groups.updated_by_user_id IS 'User who last updated the rule group.';
COMMENT ON COLUMN coupons.coupon_rule_groups.updated_by_service_identity_id IS 'Service identity that last updated the rule group.';
COMMENT ON COLUMN coupons.coupon_rule_groups.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- coupons.coupon_rules
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS coupons.coupon_rules (

    coupon_rule_id uuid DEFAULT gen_random_uuid() NOT NULL,
    coupon_rule_group_id uuid NOT NULL,
    rule_code varchar(64) NOT NULL,
    rule_name varchar(128) NOT NULL,
    rule_type coupons.coupon_rule_type_enum NOT NULL,
    rule_operator coupons.coupon_rule_operator_enum NOT NULL,
    rule_value_text varchar(256),
    rule_value_numeric numeric(18,2),
    rule_value_boolean boolean,
    site_group_id uuid,
    site_id uuid,
    merchant_id uuid,
    rule_status coupons.coupon_rule_status_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_coupon_rules PRIMARY KEY (coupon_rule_id)
);
COMMENT ON TABLE coupons.coupon_rules IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN coupons.coupon_rules.coupon_rule_id IS 'Canonical identifier of the coupon rule.';
COMMENT ON COLUMN coupons.coupon_rules.coupon_rule_group_id IS 'Parent rule group.';
COMMENT ON COLUMN coupons.coupon_rules.rule_code IS 'Stable rule code.';
COMMENT ON COLUMN coupons.coupon_rules.rule_name IS 'Human-readable rule name.';
COMMENT ON COLUMN coupons.coupon_rules.rule_type IS 'Type of rule.';
COMMENT ON COLUMN coupons.coupon_rules.rule_operator IS 'Operator used for evaluation.';
COMMENT ON COLUMN coupons.coupon_rules.rule_value_text IS 'Text value for rule evaluation.';
COMMENT ON COLUMN coupons.coupon_rules.rule_value_numeric IS 'Numeric value for rule evaluation.';
COMMENT ON COLUMN coupons.coupon_rules.rule_value_boolean IS 'Boolean value for rule evaluation.';
COMMENT ON COLUMN coupons.coupon_rules.site_group_id IS 'Site group scope where the rule applies.';
COMMENT ON COLUMN coupons.coupon_rules.site_id IS 'Site scope where the rule applies.';
COMMENT ON COLUMN coupons.coupon_rules.merchant_id IS 'Merchant scope where the rule applies.';
COMMENT ON COLUMN coupons.coupon_rules.rule_status IS 'Rule lifecycle status.';
COMMENT ON COLUMN coupons.coupon_rules.effective_from IS 'Start of rule effectiveness.';
COMMENT ON COLUMN coupons.coupon_rules.effective_to IS 'End of rule effectiveness.';
COMMENT ON COLUMN coupons.coupon_rules.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN coupons.coupon_rules.created_by_user_id IS 'User who created the rule.';
COMMENT ON COLUMN coupons.coupon_rules.created_by_service_identity_id IS 'Service identity that created the rule.';
COMMENT ON COLUMN coupons.coupon_rules.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN coupons.coupon_rules.updated_by_user_id IS 'User who last updated the rule.';
COMMENT ON COLUMN coupons.coupon_rules.updated_by_service_identity_id IS 'Service identity that last updated the rule.';
COMMENT ON COLUMN coupons.coupon_rules.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- coupons.coupon_applications
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS coupons.coupon_applications (

    coupon_application_id uuid DEFAULT gen_random_uuid() NOT NULL,
    coupon_id uuid NOT NULL,
    merchant_id uuid NOT NULL,
    merchant_wallet_id uuid,
    parking_session_id uuid NOT NULL,
    tariff_snapshot_id uuid,
    payment_attempt_id uuid,
    idempotency_key varchar(128) NOT NULL,
    application_status coupons.coupon_application_status_enum NOT NULL,
    currency_code char(3) NOT NULL,
    gross_amount_at_application numeric(18,2) NOT NULL,
    coupon_discount_amount numeric(18,2) NOT NULL,
    net_amount_after_coupon numeric(18,2) NOT NULL,
    reservation_ref varchar(128),
    reserved_at timestamptz,
    reservation_expires_at timestamptz,
    applied_at timestamptz,
    committed_at timestamptz,
    released_at timestamptz,
    expired_at timestamptz,
    rejected_at timestamptz,
    cancelled_at timestamptz,
    reversed_at timestamptz,
    rejection_reason_code varchar(64),
    release_reason_code varchar(64),
    reversal_reason_code varchar(64),
    requested_by_user_id uuid,
    requested_by_service_identity_id uuid,
    approved_by_user_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_coupon_applications PRIMARY KEY (coupon_application_id)
);
COMMENT ON TABLE coupons.coupon_applications IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN coupons.coupon_applications.coupon_application_id IS 'Canonical identifier of the coupon application.';
COMMENT ON COLUMN coupons.coupon_applications.coupon_id IS 'Coupon being applied.';
COMMENT ON COLUMN coupons.coupon_applications.merchant_id IS 'Merchant sponsor of the coupon application.';
COMMENT ON COLUMN coupons.coupon_applications.merchant_wallet_id IS 'Merchant wallet or funding context backing the application.';
COMMENT ON COLUMN coupons.coupon_applications.parking_session_id IS 'Parking session to which the coupon is applied.';
COMMENT ON COLUMN coupons.coupon_applications.tariff_snapshot_id IS 'Tariff snapshot in which the coupon effect was materialized.';
COMMENT ON COLUMN coupons.coupon_applications.payment_attempt_id IS 'Payment attempt whose finality governs commit.';
COMMENT ON COLUMN coupons.coupon_applications.idempotency_key IS 'Idempotency key for coupon application request.';
COMMENT ON COLUMN coupons.coupon_applications.application_status IS 'Coupon application lifecycle state.';
COMMENT ON COLUMN coupons.coupon_applications.currency_code IS 'Currency code.';
COMMENT ON COLUMN coupons.coupon_applications.gross_amount_at_application IS 'Gross amount when coupon was evaluated.';
COMMENT ON COLUMN coupons.coupon_applications.coupon_discount_amount IS 'Coupon amount applied or reserved.';
COMMENT ON COLUMN coupons.coupon_applications.net_amount_after_coupon IS 'Amount after coupon effect, before or after other allowed effects depending on flow.';
COMMENT ON COLUMN coupons.coupon_applications.reservation_ref IS 'Internal or wallet reservation reference.';
COMMENT ON COLUMN coupons.coupon_applications.reserved_at IS 'Timestamp when coupon value was reserved.';
COMMENT ON COLUMN coupons.coupon_applications.reservation_expires_at IS 'Reservation expiry timestamp.';
COMMENT ON COLUMN coupons.coupon_applications.applied_at IS 'Timestamp when coupon effect was applied to payable basis.';
COMMENT ON COLUMN coupons.coupon_applications.committed_at IS 'Timestamp when coupon usage was committed after confirmed payment finality.';
COMMENT ON COLUMN coupons.coupon_applications.released_at IS 'Timestamp when reservation was released.';
COMMENT ON COLUMN coupons.coupon_applications.expired_at IS 'Timestamp when application or reservation expired.';
COMMENT ON COLUMN coupons.coupon_applications.rejected_at IS 'Timestamp when application was rejected.';
COMMENT ON COLUMN coupons.coupon_applications.cancelled_at IS 'Timestamp when application was cancelled.';
COMMENT ON COLUMN coupons.coupon_applications.reversed_at IS 'Timestamp when committed application was reversed, if supported.';
COMMENT ON COLUMN coupons.coupon_applications.rejection_reason_code IS 'Controlled rejection reason.';
COMMENT ON COLUMN coupons.coupon_applications.release_reason_code IS 'Controlled release reason.';
COMMENT ON COLUMN coupons.coupon_applications.reversal_reason_code IS 'Controlled reversal reason.';
COMMENT ON COLUMN coupons.coupon_applications.requested_by_user_id IS 'User who requested the coupon application.';
COMMENT ON COLUMN coupons.coupon_applications.requested_by_service_identity_id IS 'Service identity that requested the application.';
COMMENT ON COLUMN coupons.coupon_applications.approved_by_user_id IS 'Approver for elevated coupon or full-waiver use.';
COMMENT ON COLUMN coupons.coupon_applications.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN coupons.coupon_applications.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN coupons.coupon_applications.created_by_user_id IS 'User who created the record.';
COMMENT ON COLUMN coupons.coupon_applications.created_by_service_identity_id IS 'Service identity that created the record.';
COMMENT ON COLUMN coupons.coupon_applications.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN coupons.coupon_applications.updated_by_user_id IS 'User who last updated the record.';
COMMENT ON COLUMN coupons.coupon_applications.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN coupons.coupon_applications.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- discounts.discount_policy_references
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS discounts.discount_policy_references (

    discount_policy_reference_id uuid DEFAULT gen_random_uuid() NOT NULL,
    policy_code varchar(64) NOT NULL,
    policy_name varchar(256) NOT NULL,
    policy_description text,
    policy_type discounts.discount_policy_type_enum NOT NULL,
    policy_level discounts.discount_policy_level_enum NOT NULL,
    entitlement_type discounts.statutory_entitlement_type_enum NOT NULL,
    national_law_reference varchar(128),
    local_ordinance_reference varchar(128),
    lgu_code varchar(32),
    jurisdiction_name varchar(128),
    site_group_id uuid,
    site_id uuid,
    parent_policy_reference_id uuid,
    fallback_policy_reference_id uuid,
    precedence_rank integer NOT NULL,
    policy_version varchar(32) NOT NULL,
    requires_operator_validation boolean DEFAULT false NOT NULL,
    requires_evidence_capture boolean DEFAULT false NOT NULL,
    evidence_retention_policy_code varchar(64),
    policy_status discounts.discount_policy_status_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_discount_policy_references PRIMARY KEY (discount_policy_reference_id)
);
COMMENT ON TABLE discounts.discount_policy_references IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN discounts.discount_policy_references.discount_policy_reference_id IS 'Canonical identifier of the discount policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.policy_code IS 'Stable internal policy code.';
COMMENT ON COLUMN discounts.discount_policy_references.policy_name IS 'Human-readable policy name.';
COMMENT ON COLUMN discounts.discount_policy_references.policy_description IS 'Description of the policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.policy_type IS 'Type of policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.policy_level IS 'Legal or operational level of the policy.';
COMMENT ON COLUMN discounts.discount_policy_references.entitlement_type IS 'Statutory entitlement category governed by the policy.';
COMMENT ON COLUMN discounts.discount_policy_references.national_law_reference IS 'National law reference where applicable.';
COMMENT ON COLUMN discounts.discount_policy_references.local_ordinance_reference IS 'Local ordinance number or code where applicable.';
COMMENT ON COLUMN discounts.discount_policy_references.lgu_code IS 'LGU or jurisdiction code where applicable.';
COMMENT ON COLUMN discounts.discount_policy_references.jurisdiction_name IS 'Human-readable jurisdiction name.';
COMMENT ON COLUMN discounts.discount_policy_references.site_group_id IS 'Site group scope where policy applies.';
COMMENT ON COLUMN discounts.discount_policy_references.site_id IS 'Site scope where policy applies.';
COMMENT ON COLUMN discounts.discount_policy_references.parent_policy_reference_id IS 'Parent policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.fallback_policy_reference_id IS 'Fallback policy reference, usually national law.';
COMMENT ON COLUMN discounts.discount_policy_references.precedence_rank IS 'Policy precedence within applicable scope.';
COMMENT ON COLUMN discounts.discount_policy_references.policy_version IS 'Policy version or controlled implementation version.';
COMMENT ON COLUMN discounts.discount_policy_references.requires_operator_validation IS 'Indicates whether assisted operator validation is required.';
COMMENT ON COLUMN discounts.discount_policy_references.requires_evidence_capture IS 'Indicates whether evidence reference is required.';
COMMENT ON COLUMN discounts.discount_policy_references.evidence_retention_policy_code IS 'Retention policy code for evidence.';
COMMENT ON COLUMN discounts.discount_policy_references.policy_status IS 'Policy lifecycle status.';
COMMENT ON COLUMN discounts.discount_policy_references.effective_from IS 'Start of policy effectiveness.';
COMMENT ON COLUMN discounts.discount_policy_references.effective_to IS 'End of policy effectiveness.';
COMMENT ON COLUMN discounts.discount_policy_references.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN discounts.discount_policy_references.created_by_user_id IS 'User who created the policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.created_by_service_identity_id IS 'Service identity that created the policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN discounts.discount_policy_references.updated_by_user_id IS 'User who last updated the policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.updated_by_service_identity_id IS 'Service identity that last updated the policy reference.';
COMMENT ON COLUMN discounts.discount_policy_references.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- discounts.statutory_discount_validations
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS discounts.statutory_discount_validations (

    statutory_discount_validation_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parking_session_id uuid NOT NULL,
    tariff_snapshot_id uuid,
    entitlement_type discounts.statutory_entitlement_type_enum NOT NULL,
    evaluated_policy_reference_id uuid,
    applied_policy_reference_id uuid,
    fallback_policy_reference_id uuid,
    policy_resolution_basis discounts.policy_resolution_basis_enum NOT NULL,
    local_ordinance_applied boolean DEFAULT false NOT NULL,
    national_law_fallback_applied boolean DEFAULT false NOT NULL,
    validation_channel discounts.statutory_discount_validations_channel_enum NOT NULL,
    validation_status discounts.statutory_discount_validations_status_enum NOT NULL,
    currency_code char(3),
    gross_amount_at_validation numeric(18,2),
    statutory_discount_amount numeric(18,2),
    net_amount_after_discount numeric(18,2),
    evidence_required boolean DEFAULT false NOT NULL,
    evidence_captured boolean DEFAULT false NOT NULL,
    decision_reason_code varchar(64),
    failure_reason_code varchar(64),
    requested_at timestamptz NOT NULL,
    validated_at timestamptz,
    expires_at timestamptz,
    validated_by_user_id uuid,
    validated_by_service_identity_id uuid,
    requested_by_user_id uuid,
    requested_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_statutory_discount_validations PRIMARY KEY (statutory_discount_validation_id)
);
COMMENT ON TABLE discounts.statutory_discount_validations IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN discounts.statutory_discount_validations.statutory_discount_validation_id IS 'Canonical identifier of the statutory discount validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.parking_session_id IS 'Parking session for which validation was requested.';
COMMENT ON COLUMN discounts.statutory_discount_validations.tariff_snapshot_id IS 'Tariff snapshot where approved discount effect was materialized.';
COMMENT ON COLUMN discounts.statutory_discount_validations.entitlement_type IS 'Entitlement category requested.';
COMMENT ON COLUMN discounts.statutory_discount_validations.evaluated_policy_reference_id IS 'Policy initially evaluated.';
COMMENT ON COLUMN discounts.statutory_discount_validations.applied_policy_reference_id IS 'Final policy applied to the decision.';
COMMENT ON COLUMN discounts.statutory_discount_validations.fallback_policy_reference_id IS 'Fallback policy used, usually national law.';
COMMENT ON COLUMN discounts.statutory_discount_validations.policy_resolution_basis IS 'How policy was selected.';
COMMENT ON COLUMN discounts.statutory_discount_validations.local_ordinance_applied IS 'Indicates whether a local ordinance governed the validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.national_law_fallback_applied IS 'Indicates whether national law fallback was used.';
COMMENT ON COLUMN discounts.statutory_discount_validations.validation_channel IS 'Channel used for validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.validation_status IS 'Validation lifecycle state.';
COMMENT ON COLUMN discounts.statutory_discount_validations.currency_code IS 'Currency code for discount amount fields.';
COMMENT ON COLUMN discounts.statutory_discount_validations.gross_amount_at_validation IS 'Gross amount at time of validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.statutory_discount_amount IS 'Approved statutory discount amount.';
COMMENT ON COLUMN discounts.statutory_discount_validations.net_amount_after_discount IS 'Amount after approved statutory discount.';
COMMENT ON COLUMN discounts.statutory_discount_validations.evidence_required IS 'Indicates whether evidence was required.';
COMMENT ON COLUMN discounts.statutory_discount_validations.evidence_captured IS 'Indicates whether evidence reference exists or was captured.';
COMMENT ON COLUMN discounts.statutory_discount_validations.decision_reason_code IS 'Controlled decision reason.';
COMMENT ON COLUMN discounts.statutory_discount_validations.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN discounts.statutory_discount_validations.requested_at IS 'Timestamp when validation was requested.';
COMMENT ON COLUMN discounts.statutory_discount_validations.validated_at IS 'Timestamp when validation decision was completed.';
COMMENT ON COLUMN discounts.statutory_discount_validations.expires_at IS 'Expiry timestamp for validation usability.';
COMMENT ON COLUMN discounts.statutory_discount_validations.validated_by_user_id IS 'Human operator who performed validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.validated_by_service_identity_id IS 'Service identity that performed validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.requested_by_user_id IS 'User who requested the validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.requested_by_service_identity_id IS 'Service identity that requested the validation.';
COMMENT ON COLUMN discounts.statutory_discount_validations.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN discounts.statutory_discount_validations.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN discounts.statutory_discount_validations.created_by_user_id IS 'User who created the record.';
COMMENT ON COLUMN discounts.statutory_discount_validations.created_by_service_identity_id IS 'Service identity that created the record.';
COMMENT ON COLUMN discounts.statutory_discount_validations.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN discounts.statutory_discount_validations.updated_by_user_id IS 'User who last updated the record.';
COMMENT ON COLUMN discounts.statutory_discount_validations.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN discounts.statutory_discount_validations.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- discounts.discount_evidence_references
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS discounts.discount_evidence_references (

    discount_evidence_reference_id uuid DEFAULT gen_random_uuid() NOT NULL,
    statutory_discount_validation_id uuid NOT NULL,
    evidence_type discounts.discount_evidence_type_enum NOT NULL,
    evidence_storage_type discounts.evidence_storage_type_enum NOT NULL,
    evidence_storage_ref varchar(256),
    evidence_hash char(64),
    evidence_capture_status discounts.evidence_capture_status_enum NOT NULL,
    access_classification discounts.evidence_access_classification_enum NOT NULL,
    redaction_status discounts.evidence_redaction_status_enum NOT NULL,
    retention_policy_code varchar(64) NOT NULL,
    retention_expires_at timestamptz,
    captured_at timestamptz NOT NULL,
    captured_by_user_id uuid,
    captured_by_service_identity_id uuid,
    purged_at timestamptz,
    purged_by_user_id uuid,
    purged_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_discount_evidence_references PRIMARY KEY (discount_evidence_reference_id)
);
COMMENT ON TABLE discounts.discount_evidence_references IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN discounts.discount_evidence_references.discount_evidence_reference_id IS 'Canonical identifier of the evidence reference.';
COMMENT ON COLUMN discounts.discount_evidence_references.statutory_discount_validation_id IS 'Validation record supported by this evidence reference.';
COMMENT ON COLUMN discounts.discount_evidence_references.evidence_type IS 'Type of evidence referenced.';
COMMENT ON COLUMN discounts.discount_evidence_references.evidence_storage_type IS 'Storage mechanism or reference type.';
COMMENT ON COLUMN discounts.discount_evidence_references.evidence_storage_ref IS 'Object key, evidence vault reference, URI reference, or controlled storage reference.';
COMMENT ON COLUMN discounts.discount_evidence_references.evidence_hash IS 'Hash of evidence content where retained.';
COMMENT ON COLUMN discounts.discount_evidence_references.evidence_capture_status IS 'Evidence reference lifecycle state.';
COMMENT ON COLUMN discounts.discount_evidence_references.access_classification IS 'Access classification.';
COMMENT ON COLUMN discounts.discount_evidence_references.redaction_status IS 'Redaction or minimization state.';
COMMENT ON COLUMN discounts.discount_evidence_references.retention_policy_code IS 'Retention policy applied to the evidence.';
COMMENT ON COLUMN discounts.discount_evidence_references.retention_expires_at IS 'Date/time when evidence becomes eligible for purge or redaction.';
COMMENT ON COLUMN discounts.discount_evidence_references.captured_at IS 'Timestamp when evidence reference was captured.';
COMMENT ON COLUMN discounts.discount_evidence_references.captured_by_user_id IS 'User who captured the evidence.';
COMMENT ON COLUMN discounts.discount_evidence_references.captured_by_service_identity_id IS 'Service identity that captured the evidence.';
COMMENT ON COLUMN discounts.discount_evidence_references.purged_at IS 'Timestamp when evidence payload was purged, if applicable.';
COMMENT ON COLUMN discounts.discount_evidence_references.purged_by_user_id IS 'User who purged the evidence.';
COMMENT ON COLUMN discounts.discount_evidence_references.purged_by_service_identity_id IS 'Service identity that purged the evidence.';
COMMENT ON COLUMN discounts.discount_evidence_references.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN discounts.discount_evidence_references.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN discounts.discount_evidence_references.created_by_user_id IS 'User who created the reference.';
COMMENT ON COLUMN discounts.discount_evidence_references.created_by_service_identity_id IS 'Service identity that created the reference.';
COMMENT ON COLUMN discounts.discount_evidence_references.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN discounts.discount_evidence_references.updated_by_user_id IS 'User who last updated the reference.';
COMMENT ON COLUMN discounts.discount_evidence_references.updated_by_service_identity_id IS 'Service identity that last updated the reference.';
COMMENT ON COLUMN discounts.discount_evidence_references.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- gates.gate_devices
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS gates.gate_devices (

    gate_device_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_id uuid NOT NULL,
    lane_id uuid,
    service_identity_id uuid,
    device_code varchar(64) NOT NULL,
    device_name varchar(128) NOT NULL,
    device_type gates.gate_device_type_enum NOT NULL,
    vendor_device_ref varchar(128),
    serial_number varchar(128),
    device_status gates.gate_device_status_enum NOT NULL,
    installed_at timestamptz,
    activated_at timestamptz,
    retired_at timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_gate_devices PRIMARY KEY (gate_device_id)
);
COMMENT ON TABLE gates.gate_devices IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN gates.gate_devices.gate_device_id IS 'Canonical identifier of the gate device.';
COMMENT ON COLUMN gates.gate_devices.site_id IS 'Site where the device operates.';
COMMENT ON COLUMN gates.gate_devices.lane_id IS 'Lane where the device is assigned.';
COMMENT ON COLUMN gates.gate_devices.service_identity_id IS 'Authenticated service or device principal.';
COMMENT ON COLUMN gates.gate_devices.device_code IS 'Stable internal device code.';
COMMENT ON COLUMN gates.gate_devices.device_name IS 'Human-readable device name.';
COMMENT ON COLUMN gates.gate_devices.device_type IS 'Device classification.';
COMMENT ON COLUMN gates.gate_devices.vendor_device_ref IS 'Vendor or controller reference for the device.';
COMMENT ON COLUMN gates.gate_devices.serial_number IS 'Manufacturer serial number where available.';
COMMENT ON COLUMN gates.gate_devices.device_status IS 'Device lifecycle or operational status.';
COMMENT ON COLUMN gates.gate_devices.installed_at IS 'Installation timestamp.';
COMMENT ON COLUMN gates.gate_devices.activated_at IS 'Activation timestamp.';
COMMENT ON COLUMN gates.gate_devices.retired_at IS 'Retirement timestamp.';
COMMENT ON COLUMN gates.gate_devices.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN gates.gate_devices.created_by_user_id IS 'User who created the record.';
COMMENT ON COLUMN gates.gate_devices.created_by_service_identity_id IS 'Service identity that created the record.';
COMMENT ON COLUMN gates.gate_devices.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN gates.gate_devices.updated_by_user_id IS 'User who last updated the record.';
COMMENT ON COLUMN gates.gate_devices.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN gates.gate_devices.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- gates.gate_authorization_consumptions
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS gates.gate_authorization_consumptions (

    gate_authorization_consumption_id uuid DEFAULT gen_random_uuid() NOT NULL,
    exit_authorization_id uuid,
    authorization_token_hash char(64),
    gate_device_id uuid,
    site_id uuid NOT NULL,
    lane_id uuid,
    consume_status gates.gate_authorization_consumption_status_enum NOT NULL,
    consume_reason_code varchar(64),
    requested_at timestamptz NOT NULL,
    validated_at timestamptz,
    consumed_at timestamptz,
    command_requested boolean DEFAULT false NOT NULL,
    command_result_status gates.gate_command_result_status_enum,
    command_result_at timestamptz,
    failure_detail text,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_gate_authorization_consumptions PRIMARY KEY (gate_authorization_consumption_id)
);
COMMENT ON TABLE gates.gate_authorization_consumptions IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.gate_authorization_consumption_id IS 'Canonical identifier of the consume attempt.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.exit_authorization_id IS 'Authorization presented or consumed, where known.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.authorization_token_hash IS 'Hash of presented token where authorization ID is not yet known or for replay analysis.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.gate_device_id IS 'Gate device involved in the consume attempt.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.site_id IS 'Site where consume attempt occurred.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.lane_id IS 'Lane where consume attempt occurred.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.consume_status IS 'Consume result.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.consume_reason_code IS 'Controlled reason for denial, failure, or exception.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.requested_at IS 'Timestamp when consume request was received.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.validated_at IS 'Timestamp when authorization validation completed.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.consumed_at IS 'Timestamp when authorization was consumed through the approved path.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.command_requested IS 'Indicates whether gate-open command was requested after consume.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.command_result_status IS 'Result of gate-open command, if captured here.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.command_result_at IS 'Timestamp when command result was known.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.failure_detail IS 'Controlled troubleshooting detail.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.created_by_service_identity_id IS 'Service identity that recorded the consume attempt.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.updated_by_service_identity_id IS 'Service identity that updated the consume record.';
COMMENT ON COLUMN gates.gate_authorization_consumptions.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- gates.gate_events
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS gates.gate_events (

    gate_event_id uuid DEFAULT gen_random_uuid() NOT NULL,
    gate_device_id uuid,
    gate_authorization_consumption_id uuid,
    exit_authorization_id uuid,
    site_id uuid NOT NULL,
    lane_id uuid,
    event_type gates.gate_event_type_enum NOT NULL,
    event_status gates.gate_event_status_enum NOT NULL,
    event_reason_code varchar(64),
    event_payload_ref varchar(256),
    event_payload_hash char(64),
    source_event_ref varchar(128),
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    CONSTRAINT pk_gate_events PRIMARY KEY (gate_event_id)
);
COMMENT ON TABLE gates.gate_events IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN gates.gate_events.gate_event_id IS 'Canonical identifier of the gate event.';
COMMENT ON COLUMN gates.gate_events.gate_device_id IS 'Gate device that emitted or was associated with the event.';
COMMENT ON COLUMN gates.gate_events.gate_authorization_consumption_id IS 'Related authorization consume attempt.';
COMMENT ON COLUMN gates.gate_events.exit_authorization_id IS 'Related exit authorization where applicable.';
COMMENT ON COLUMN gates.gate_events.site_id IS 'Site where the event occurred.';
COMMENT ON COLUMN gates.gate_events.lane_id IS 'Lane where the event occurred.';
COMMENT ON COLUMN gates.gate_events.event_type IS 'Gate event type.';
COMMENT ON COLUMN gates.gate_events.event_status IS 'Event result or classification.';
COMMENT ON COLUMN gates.gate_events.event_reason_code IS 'Controlled reason or classification.';
COMMENT ON COLUMN gates.gate_events.event_payload_ref IS 'Reference to detailed event payload if retained.';
COMMENT ON COLUMN gates.gate_events.event_payload_hash IS 'Hash of detailed payload where retained.';
COMMENT ON COLUMN gates.gate_events.source_event_ref IS 'Vendor or device event reference where available.';
COMMENT ON COLUMN gates.gate_events.occurred_at IS 'Timestamp when event occurred or was observed.';
COMMENT ON COLUMN gates.gate_events.received_at IS 'Timestamp when ExitPass received or recorded the event.';
COMMENT ON COLUMN gates.gate_events.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN gates.gate_events.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN gates.gate_events.created_by_service_identity_id IS 'Service identity that created the event.';

-- ------------------------------------------------------------
-- gates.gate_heartbeats
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS gates.gate_heartbeats (

    gate_heartbeat_id uuid DEFAULT gen_random_uuid() NOT NULL,
    gate_device_id uuid NOT NULL,
    site_id uuid NOT NULL,
    lane_id uuid,
    heartbeat_status gates.gate_heartbeat_status_enum NOT NULL,
    device_reported_status varchar(64),
    latency_ms integer,
    error_code varchar(64),
    error_detail text,
    observed_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    CONSTRAINT pk_gate_heartbeats PRIMARY KEY (gate_heartbeat_id)
);
COMMENT ON TABLE gates.gate_heartbeats IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN gates.gate_heartbeats.gate_heartbeat_id IS 'Canonical identifier of the heartbeat record.';
COMMENT ON COLUMN gates.gate_heartbeats.gate_device_id IS 'Gate device that produced or is represented by the heartbeat.';
COMMENT ON COLUMN gates.gate_heartbeats.site_id IS 'Site where the device operates.';
COMMENT ON COLUMN gates.gate_heartbeats.lane_id IS 'Lane where the device operates.';
COMMENT ON COLUMN gates.gate_heartbeats.heartbeat_status IS 'Health status.';
COMMENT ON COLUMN gates.gate_heartbeats.device_reported_status IS 'Native status from device or adapter.';
COMMENT ON COLUMN gates.gate_heartbeats.latency_ms IS 'Measured latency in milliseconds.';
COMMENT ON COLUMN gates.gate_heartbeats.error_code IS 'Device or adapter error code.';
COMMENT ON COLUMN gates.gate_heartbeats.error_detail IS 'Controlled error detail or diagnostic note.';
COMMENT ON COLUMN gates.gate_heartbeats.observed_at IS 'Timestamp when health was observed.';
COMMENT ON COLUMN gates.gate_heartbeats.received_at IS 'Timestamp when ExitPass recorded heartbeat.';
COMMENT ON COLUMN gates.gate_heartbeats.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN gates.gate_heartbeats.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN gates.gate_heartbeats.created_by_service_identity_id IS 'Service identity that recorded the heartbeat.';

-- ------------------------------------------------------------
-- operations.manual_gate_logs
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS operations.manual_gate_logs (

    manual_gate_log_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parking_session_id uuid,
    exit_authorization_id uuid,
    gate_authorization_consumption_id uuid,
    incident_record_id uuid,
    override_approval_id uuid,
    site_id uuid NOT NULL,
    lane_id uuid,
    gate_device_id uuid,
    manual_action_type operations.manual_gate_action_type_enum NOT NULL,
    manual_action_status operations.manual_gate_action_status_enum NOT NULL,
    manual_reason_code varchar(64) NOT NULL,
    operator_notes text,
    requires_reconciliation boolean DEFAULT false NOT NULL,
    reconciliation_item_id uuid,
    performed_at timestamptz NOT NULL,
    performed_by_user_id uuid NOT NULL,
    recorded_at timestamptz DEFAULT now() NOT NULL,
    recorded_by_user_id uuid,
    recorded_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_manual_gate_logs PRIMARY KEY (manual_gate_log_id)
);
COMMENT ON TABLE operations.manual_gate_logs IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN operations.manual_gate_logs.manual_gate_log_id IS 'Canonical identifier of the manual gate log.';
COMMENT ON COLUMN operations.manual_gate_logs.parking_session_id IS 'Related parking session, where known.';
COMMENT ON COLUMN operations.manual_gate_logs.exit_authorization_id IS 'Related exit authorization, where known.';
COMMENT ON COLUMN operations.manual_gate_logs.gate_authorization_consumption_id IS 'Related failed or uncertain consume attempt, where applicable.';
COMMENT ON COLUMN operations.manual_gate_logs.incident_record_id IS 'Incident that caused or justified the manual action.';
COMMENT ON COLUMN operations.manual_gate_logs.override_approval_id IS 'Approval record authorizing the manual action, where required.';
COMMENT ON COLUMN operations.manual_gate_logs.site_id IS 'Site where manual gate action occurred.';
COMMENT ON COLUMN operations.manual_gate_logs.lane_id IS 'Lane where manual gate action occurred.';
COMMENT ON COLUMN operations.manual_gate_logs.gate_device_id IS 'Gate device involved, where known.';
COMMENT ON COLUMN operations.manual_gate_logs.manual_action_type IS 'Type of manual gate action.';
COMMENT ON COLUMN operations.manual_gate_logs.manual_action_status IS 'Result of manual action.';
COMMENT ON COLUMN operations.manual_gate_logs.manual_reason_code IS 'Controlled reason for manual action.';
COMMENT ON COLUMN operations.manual_gate_logs.operator_notes IS 'Controlled operational note. Must not store sensitive evidence casually.';
COMMENT ON COLUMN operations.manual_gate_logs.requires_reconciliation IS 'Indicates whether the action requires reconciliation or review.';
COMMENT ON COLUMN operations.manual_gate_logs.reconciliation_item_id IS 'Reconciliation item created for review or closure.';
COMMENT ON COLUMN operations.manual_gate_logs.performed_at IS 'Timestamp when manual action occurred.';
COMMENT ON COLUMN operations.manual_gate_logs.performed_by_user_id IS 'Operator who performed the manual action.';
COMMENT ON COLUMN operations.manual_gate_logs.recorded_at IS 'Timestamp when the action was recorded.';
COMMENT ON COLUMN operations.manual_gate_logs.recorded_by_user_id IS 'User who recorded the action.';
COMMENT ON COLUMN operations.manual_gate_logs.recorded_by_service_identity_id IS 'Service identity that recorded the action.';
COMMENT ON COLUMN operations.manual_gate_logs.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN operations.manual_gate_logs.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN operations.manual_gate_logs.created_by_user_id IS 'User who created the record.';
COMMENT ON COLUMN operations.manual_gate_logs.created_by_service_identity_id IS 'Service identity that created the record.';
COMMENT ON COLUMN operations.manual_gate_logs.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN operations.manual_gate_logs.updated_by_user_id IS 'User who last updated the record.';
COMMENT ON COLUMN operations.manual_gate_logs.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN operations.manual_gate_logs.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- operations.override_requests
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS operations.override_requests (

    override_request_id uuid DEFAULT gen_random_uuid() NOT NULL,
    incident_record_id uuid,
    target_entity_type varchar(64),
    target_entity_id uuid,
    site_id uuid,
    lane_id uuid,
    override_type operations.override_type_enum NOT NULL,
    override_reason_code varchar(64) NOT NULL,
    request_status operations.override_request_status_enum NOT NULL,
    request_notes text,
    requires_approval boolean DEFAULT false NOT NULL,
    requested_at timestamptz NOT NULL,
    requested_by_user_id uuid NOT NULL,
    expires_at timestamptz,
    closed_at timestamptz,
    closure_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_override_requests PRIMARY KEY (override_request_id)
);
COMMENT ON TABLE operations.override_requests IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN operations.override_requests.override_request_id IS 'Canonical identifier of the override request.';
COMMENT ON COLUMN operations.override_requests.incident_record_id IS 'Related incident, where applicable.';
COMMENT ON COLUMN operations.override_requests.target_entity_type IS 'Type of affected entity.';
COMMENT ON COLUMN operations.override_requests.target_entity_id IS 'Identifier of affected entity.';
COMMENT ON COLUMN operations.override_requests.site_id IS 'Site affected by the override request.';
COMMENT ON COLUMN operations.override_requests.lane_id IS 'Lane affected by the override request.';
COMMENT ON COLUMN operations.override_requests.override_type IS 'Type of override requested.';
COMMENT ON COLUMN operations.override_requests.override_reason_code IS 'Controlled reason for request.';
COMMENT ON COLUMN operations.override_requests.request_status IS 'Request lifecycle state.';
COMMENT ON COLUMN operations.override_requests.request_notes IS 'Controlled operational note. Must not store sensitive evidence casually.';
COMMENT ON COLUMN operations.override_requests.requires_approval IS 'Indicates whether approval is required.';
COMMENT ON COLUMN operations.override_requests.requested_at IS 'Timestamp when request was made.';
COMMENT ON COLUMN operations.override_requests.requested_by_user_id IS 'User who requested the override.';
COMMENT ON COLUMN operations.override_requests.expires_at IS 'Expiry timestamp for request validity.';
COMMENT ON COLUMN operations.override_requests.closed_at IS 'Closure timestamp.';
COMMENT ON COLUMN operations.override_requests.closure_reason_code IS 'Controlled closure reason.';
COMMENT ON COLUMN operations.override_requests.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN operations.override_requests.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN operations.override_requests.created_by_user_id IS 'User who created the request.';
COMMENT ON COLUMN operations.override_requests.created_by_service_identity_id IS 'Service identity that created the request.';
COMMENT ON COLUMN operations.override_requests.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN operations.override_requests.updated_by_user_id IS 'User who last updated the request.';
COMMENT ON COLUMN operations.override_requests.updated_by_service_identity_id IS 'Service identity that last updated the request.';
COMMENT ON COLUMN operations.override_requests.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- operations.override_approvals
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS operations.override_approvals (

    override_approval_id uuid DEFAULT gen_random_uuid() NOT NULL,
    override_request_id uuid NOT NULL,
    approval_sequence integer NOT NULL,
    approval_decision operations.override_approval_decision_enum NOT NULL,
    approval_reason_code varchar(64),
    rejection_reason_code varchar(64),
    approval_notes text,
    decided_at timestamptz NOT NULL,
    decided_by_user_id uuid NOT NULL,
    expires_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_override_approvals PRIMARY KEY (override_approval_id)
);
COMMENT ON TABLE operations.override_approvals IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN operations.override_approvals.override_approval_id IS 'Canonical identifier of the approval record.';
COMMENT ON COLUMN operations.override_approvals.override_request_id IS 'Override request being reviewed.';
COMMENT ON COLUMN operations.override_approvals.approval_sequence IS 'Approval sequence or level.';
COMMENT ON COLUMN operations.override_approvals.approval_decision IS 'Approval decision.';
COMMENT ON COLUMN operations.override_approvals.approval_reason_code IS 'Controlled approval reason.';
COMMENT ON COLUMN operations.override_approvals.rejection_reason_code IS 'Controlled rejection reason.';
COMMENT ON COLUMN operations.override_approvals.approval_notes IS 'Controlled note. Must not store sensitive evidence casually.';
COMMENT ON COLUMN operations.override_approvals.decided_at IS 'Timestamp when decision was made.';
COMMENT ON COLUMN operations.override_approvals.decided_by_user_id IS 'User who approved, rejected, escalated, or cancelled.';
COMMENT ON COLUMN operations.override_approvals.expires_at IS 'Expiry timestamp for approval usability.';
COMMENT ON COLUMN operations.override_approvals.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN operations.override_approvals.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN operations.override_approvals.created_by_user_id IS 'User who created the approval record.';
COMMENT ON COLUMN operations.override_approvals.created_by_service_identity_id IS 'Service identity that created the approval record.';

-- ------------------------------------------------------------
-- operations.incident_records
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS operations.incident_records (

    incident_record_id uuid DEFAULT gen_random_uuid() NOT NULL,
    incident_code varchar(64) NOT NULL,
    incident_title varchar(256) NOT NULL,
    incident_description text,
    incident_category varchar(64) NOT NULL,
    incident_severity operations.incident_severity_enum NOT NULL,
    incident_status operations.incident_status_enum NOT NULL,
    site_group_id uuid,
    site_id uuid,
    lane_id uuid,
    gate_device_id uuid,
    vendor_system_id uuid,
    payment_rail_id uuid,
    started_at timestamptz DEFAULT now() NOT NULL,
    detected_at timestamptz NOT NULL,
    resolved_at timestamptz,
    closed_at timestamptz,
    closure_reason_code varchar(64),
    owner_user_id uuid,
    owner_service_identity_id uuid,
    requires_reconciliation boolean DEFAULT false NOT NULL,
    requires_post_incident_review boolean DEFAULT false NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_incident_records PRIMARY KEY (incident_record_id)
);
COMMENT ON TABLE operations.incident_records IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN operations.incident_records.incident_record_id IS 'Canonical identifier of the incident.';
COMMENT ON COLUMN operations.incident_records.incident_code IS 'Human-readable or generated incident code.';
COMMENT ON COLUMN operations.incident_records.incident_title IS 'Short incident title.';
COMMENT ON COLUMN operations.incident_records.incident_description IS 'Incident description.';
COMMENT ON COLUMN operations.incident_records.incident_category IS 'Controlled incident category.';
COMMENT ON COLUMN operations.incident_records.incident_severity IS 'Incident severity.';
COMMENT ON COLUMN operations.incident_records.incident_status IS 'Incident lifecycle state.';
COMMENT ON COLUMN operations.incident_records.site_group_id IS 'Affected site group, where applicable.';
COMMENT ON COLUMN operations.incident_records.site_id IS 'Affected site, where applicable.';
COMMENT ON COLUMN operations.incident_records.lane_id IS 'Affected lane, where applicable.';
COMMENT ON COLUMN operations.incident_records.gate_device_id IS 'Affected gate device, where applicable.';
COMMENT ON COLUMN operations.incident_records.vendor_system_id IS 'Affected vendor system, where applicable.';
COMMENT ON COLUMN operations.incident_records.payment_rail_id IS 'Affected payment rail, where applicable.';
COMMENT ON COLUMN operations.incident_records.started_at IS 'Incident start timestamp.';
COMMENT ON COLUMN operations.incident_records.detected_at IS 'Incident detection timestamp.';
COMMENT ON COLUMN operations.incident_records.resolved_at IS 'Incident technical resolution timestamp.';
COMMENT ON COLUMN operations.incident_records.closed_at IS 'Incident closure timestamp.';
COMMENT ON COLUMN operations.incident_records.closure_reason_code IS 'Controlled closure reason.';
COMMENT ON COLUMN operations.incident_records.owner_user_id IS 'User assigned as incident owner.';
COMMENT ON COLUMN operations.incident_records.owner_service_identity_id IS 'Service identity assigned as owner, where automated.';
COMMENT ON COLUMN operations.incident_records.requires_reconciliation IS 'Indicates whether incident requires reconciliation.';
COMMENT ON COLUMN operations.incident_records.requires_post_incident_review IS 'Indicates whether post-incident review is required.';
COMMENT ON COLUMN operations.incident_records.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN operations.incident_records.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN operations.incident_records.created_by_user_id IS 'User who created the incident.';
COMMENT ON COLUMN operations.incident_records.created_by_service_identity_id IS 'Service identity that created the incident.';
COMMENT ON COLUMN operations.incident_records.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN operations.incident_records.updated_by_user_id IS 'User who last updated the incident.';
COMMENT ON COLUMN operations.incident_records.updated_by_service_identity_id IS 'Service identity that last updated the incident.';
COMMENT ON COLUMN operations.incident_records.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- operations.operator_action_logs
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS operations.operator_action_logs (

    operator_action_log_id uuid DEFAULT gen_random_uuid() NOT NULL,
    operator_user_id uuid NOT NULL,
    action_type operations.operator_action_type_enum NOT NULL,
    action_reason_code varchar(64),
    target_entity_type varchar(64),
    target_entity_id uuid,
    site_id uuid,
    incident_record_id uuid,
    action_status operations.operator_action_status_enum NOT NULL,
    action_notes text,
    performed_at timestamptz NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_operator_action_logs PRIMARY KEY (operator_action_log_id)
);
COMMENT ON TABLE operations.operator_action_logs IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN operations.operator_action_logs.operator_action_log_id IS 'Canonical identifier of the operator action log.';
COMMENT ON COLUMN operations.operator_action_logs.operator_user_id IS 'User who performed the action.';
COMMENT ON COLUMN operations.operator_action_logs.action_type IS 'Type of operator action.';
COMMENT ON COLUMN operations.operator_action_logs.action_reason_code IS 'Controlled reason for the action.';
COMMENT ON COLUMN operations.operator_action_logs.target_entity_type IS 'Type of affected entity.';
COMMENT ON COLUMN operations.operator_action_logs.target_entity_id IS 'Affected entity identifier.';
COMMENT ON COLUMN operations.operator_action_logs.site_id IS 'Site context, where applicable.';
COMMENT ON COLUMN operations.operator_action_logs.incident_record_id IS 'Related incident, where applicable.';
COMMENT ON COLUMN operations.operator_action_logs.action_status IS 'Result of action.';
COMMENT ON COLUMN operations.operator_action_logs.action_notes IS 'Controlled note. Must not store sensitive evidence casually.';
COMMENT ON COLUMN operations.operator_action_logs.performed_at IS 'Timestamp when operator action occurred.';
COMMENT ON COLUMN operations.operator_action_logs.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN operations.operator_action_logs.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN operations.operator_action_logs.created_by_user_id IS 'User who created the log.';
COMMENT ON COLUMN operations.operator_action_logs.created_by_service_identity_id IS 'Service identity that created the log.';

-- ------------------------------------------------------------
-- reconciliation.mops_transaction_records
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS reconciliation.mops_transaction_records (

    mops_transaction_record_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parking_session_id uuid,
    manual_gate_log_id uuid,
    incident_record_id uuid,
    site_id uuid NOT NULL,
    lane_id uuid,
    source_system_code varchar(64) NOT NULL,
    source_transaction_ref varchar(128),
    source_batch_ref varchar(128),
    collection_reference varchar(128),
    currency_code char(3),
    amount numeric(18,2),
    payment_method_label varchar(64),
    continuity_reason_code varchar(64) NOT NULL,
    record_status reconciliation.mops_transaction_record_status_enum NOT NULL,
    captured_at timestamptz NOT NULL,
    imported_at timestamptz,
    reconciled_at timestamptz,
    rejected_at timestamptz,
    disputed_at timestamptz,
    failure_reason_code varchar(64),
    evidence_ref varchar(256),
    evidence_hash char(64),
    captured_by_user_id uuid,
    captured_by_service_identity_id uuid,
    imported_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_mops_transaction_records PRIMARY KEY (mops_transaction_record_id)
);
COMMENT ON TABLE reconciliation.mops_transaction_records IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.mops_transaction_record_id IS 'Canonical identifier of the MoPS or continuity-origin record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.parking_session_id IS 'Related parking session, where identifiable.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.manual_gate_log_id IS 'Related manual gate action, where applicable.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.incident_record_id IS 'Incident or continuity event that caused the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.site_id IS 'Site where the continuity event occurred.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.lane_id IS 'Lane where the continuity event occurred.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.source_system_code IS 'Source system or continuity tool code.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.source_transaction_ref IS 'Source transaction reference.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.source_batch_ref IS 'Import batch or source batch reference.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.collection_reference IS 'Manual or continuity collection reference.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.currency_code IS 'Currency code for amount fields.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.amount IS 'Amount captured in continuity path.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.payment_method_label IS 'Continuity-recorded payment method label.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.continuity_reason_code IS 'Controlled reason for continuity handling.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.record_status IS 'Lifecycle state of the continuity-origin record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.captured_at IS 'Timestamp when continuity event was captured.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.imported_at IS 'Timestamp when record was imported into Central PMS.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.reconciled_at IS 'Timestamp when record reached reconciled state.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.rejected_at IS 'Timestamp when record was rejected.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.disputed_at IS 'Timestamp when record was disputed.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.failure_reason_code IS 'Controlled failure or rejection reason.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.evidence_ref IS 'Reference to supporting evidence or import file.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.evidence_hash IS 'Hash of supporting evidence where applicable.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.captured_by_user_id IS 'Human actor who captured the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.captured_by_service_identity_id IS 'Service or tool identity that captured the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.imported_by_service_identity_id IS 'Service identity that imported the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.created_by_user_id IS 'User who created the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.created_by_service_identity_id IS 'Service identity that created the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.updated_by_user_id IS 'User who last updated the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN reconciliation.mops_transaction_records.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- reconciliation.reconciliation_runs
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS reconciliation.reconciliation_runs (

    reconciliation_run_id uuid DEFAULT gen_random_uuid() NOT NULL,
    run_code varchar(64) NOT NULL,
    run_type reconciliation.reconciliation_run_type_enum NOT NULL,
    run_status reconciliation.reconciliation_run_status_enum NOT NULL,
    scope_type reconciliation.reconciliation_scope_type_enum NOT NULL,
    site_group_id uuid,
    site_id uuid,
    incident_record_id uuid,
    payment_rail_id uuid,
    vendor_system_id uuid,
    source_batch_ref varchar(128),
    window_start_at timestamptz,
    window_end_at timestamptz,
    started_at timestamptz DEFAULT now() NOT NULL,
    completed_at timestamptz,
    failed_at timestamptz,
    failure_reason_code varchar(64),
    item_count integer NOT NULL,
    matched_count integer NOT NULL,
    exception_count integer NOT NULL,
    rejected_count integer NOT NULL,
    disputed_count integer NOT NULL,
    initiated_by_user_id uuid,
    initiated_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_reconciliation_runs PRIMARY KEY (reconciliation_run_id)
);
COMMENT ON TABLE reconciliation.reconciliation_runs IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.reconciliation_run_id IS 'Canonical identifier of the reconciliation run.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.run_code IS 'Human-readable or generated run code.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.run_type IS 'Type of reconciliation run.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.run_status IS 'Run lifecycle state.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.scope_type IS 'Scope type for the run.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.site_group_id IS 'Site group scope.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.site_id IS 'Site scope.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.incident_record_id IS 'Incident being reconciled, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.payment_rail_id IS 'Payment rail scope, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.vendor_system_id IS 'Vendor system scope, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.source_batch_ref IS 'Source batch reference being reconciled.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.window_start_at IS 'Reconciliation window start.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.window_end_at IS 'Reconciliation window end.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.started_at IS 'Run start timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.completed_at IS 'Run completion timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.failed_at IS 'Run failure timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.item_count IS 'Total item count.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.matched_count IS 'Matched item count.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.exception_count IS 'Exception item count.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.rejected_count IS 'Rejected item count.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.disputed_count IS 'Disputed item count.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.initiated_by_user_id IS 'User who initiated the run.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.initiated_by_service_identity_id IS 'Service identity that initiated the run.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.created_by_user_id IS 'User who created the run record.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.created_by_service_identity_id IS 'Service identity that created the run record.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.updated_by_user_id IS 'User who last updated the run record.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.updated_by_service_identity_id IS 'Service identity that last updated the run record.';
COMMENT ON COLUMN reconciliation.reconciliation_runs.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- reconciliation.reconciliation_items
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS reconciliation.reconciliation_items (

    reconciliation_item_id uuid DEFAULT gen_random_uuid() NOT NULL,
    reconciliation_run_id uuid NOT NULL,
    mops_transaction_record_id uuid,
    manual_gate_log_id uuid,
    payment_attempt_id uuid,
    payment_confirmation_id uuid,
    provider_outcome_id uuid,
    target_entity_type varchar(64),
    target_entity_id uuid,
    comparison_basis reconciliation.reconciliation_comparison_basis_enum NOT NULL,
    item_status reconciliation.reconciliation_item_status_enum NOT NULL,
    match_status reconciliation.reconciliation_match_status_enum NOT NULL,
    expected_amount numeric(18,2),
    actual_amount numeric(18,2),
    currency_code char(3),
    variance_amount numeric(18,2),
    exception_reason_code varchar(64),
    resolved_at timestamptz,
    resolved_by_user_id uuid,
    resolved_by_service_identity_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    correlation_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_reconciliation_items PRIMARY KEY (reconciliation_item_id)
);
COMMENT ON TABLE reconciliation.reconciliation_items IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN reconciliation.reconciliation_items.reconciliation_item_id IS 'Canonical identifier of the reconciliation item.';
COMMENT ON COLUMN reconciliation.reconciliation_items.reconciliation_run_id IS 'Parent reconciliation run.';
COMMENT ON COLUMN reconciliation.reconciliation_items.mops_transaction_record_id IS 'MoPS or continuity-origin record being reconciled.';
COMMENT ON COLUMN reconciliation.reconciliation_items.manual_gate_log_id IS 'Manual gate log being reconciled, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_items.payment_attempt_id IS 'Related payment attempt, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_items.payment_confirmation_id IS 'Related payment confirmation, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_items.provider_outcome_id IS 'Related provider outcome, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_items.target_entity_type IS 'Generic target entity type where a specific FK is not available.';
COMMENT ON COLUMN reconciliation.reconciliation_items.target_entity_id IS 'Generic target entity ID where a specific FK is not available.';
COMMENT ON COLUMN reconciliation.reconciliation_items.comparison_basis IS 'Basis used for comparison.';
COMMENT ON COLUMN reconciliation.reconciliation_items.item_status IS 'Item-level reconciliation outcome.';
COMMENT ON COLUMN reconciliation.reconciliation_items.match_status IS 'Match classification.';
COMMENT ON COLUMN reconciliation.reconciliation_items.expected_amount IS 'Expected amount, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_items.actual_amount IS 'Actual amount from compared evidence.';
COMMENT ON COLUMN reconciliation.reconciliation_items.currency_code IS 'Currency code for amount comparison.';
COMMENT ON COLUMN reconciliation.reconciliation_items.variance_amount IS 'Difference between expected and actual amount.';
COMMENT ON COLUMN reconciliation.reconciliation_items.exception_reason_code IS 'Controlled exception reason.';
COMMENT ON COLUMN reconciliation.reconciliation_items.resolved_at IS 'Timestamp when item was resolved.';
COMMENT ON COLUMN reconciliation.reconciliation_items.resolved_by_user_id IS 'User who resolved the item.';
COMMENT ON COLUMN reconciliation.reconciliation_items.resolved_by_service_identity_id IS 'Service identity that resolved the item.';
COMMENT ON COLUMN reconciliation.reconciliation_items.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_items.created_by_user_id IS 'User who created the item.';
COMMENT ON COLUMN reconciliation.reconciliation_items.created_by_service_identity_id IS 'Service identity that created the item.';
COMMENT ON COLUMN reconciliation.reconciliation_items.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_items.updated_by_user_id IS 'User who last updated the item.';
COMMENT ON COLUMN reconciliation.reconciliation_items.updated_by_service_identity_id IS 'Service identity that last updated the item.';
COMMENT ON COLUMN reconciliation.reconciliation_items.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN reconciliation.reconciliation_items.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- reconciliation.reconciliation_exceptions
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS reconciliation.reconciliation_exceptions (

    reconciliation_exception_id uuid DEFAULT gen_random_uuid() NOT NULL,
    reconciliation_run_id uuid NOT NULL,
    reconciliation_item_id uuid,
    incident_record_id uuid,
    exception_type reconciliation.reconciliation_exception_type_enum NOT NULL,
    exception_severity reconciliation.reconciliation_exception_severity_enum NOT NULL,
    exception_status reconciliation.reconciliation_exception_status_enum NOT NULL,
    exception_reason_code varchar(64) NOT NULL,
    exception_summary varchar(256) NOT NULL,
    exception_detail text,
    assigned_to_user_id uuid,
    assigned_to_service_identity_id uuid,
    created_from_status varchar(64),
    detected_at timestamptz NOT NULL,
    assigned_at timestamptz DEFAULT now(),
    resolved_at timestamptz,
    closed_at timestamptz,
    resolution_reason_code varchar(64),
    closure_reason_code varchar(64),
    resolved_by_user_id uuid,
    resolved_by_service_identity_id uuid,
    closed_by_user_id uuid,
    closed_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_reconciliation_exceptions PRIMARY KEY (reconciliation_exception_id)
);
COMMENT ON TABLE reconciliation.reconciliation_exceptions IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.reconciliation_exception_id IS 'Canonical identifier of the reconciliation exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.reconciliation_run_id IS 'Run where the exception was discovered.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.reconciliation_item_id IS 'Item that produced the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.incident_record_id IS 'Related incident, where applicable.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.exception_type IS 'Type of reconciliation exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.exception_severity IS 'Severity of exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.exception_status IS 'Exception lifecycle state.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.exception_reason_code IS 'Controlled reason for exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.exception_summary IS 'Short human-readable summary.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.exception_detail IS 'Controlled detailed note. Must not store sensitive evidence casually.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.assigned_to_user_id IS 'User assigned to resolve the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.assigned_to_service_identity_id IS 'Service identity assigned to resolve the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.created_from_status IS 'Item or source status that triggered the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.detected_at IS 'Timestamp when exception was detected.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.assigned_at IS 'Timestamp when exception was assigned.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.resolved_at IS 'Timestamp when exception was resolved.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.closed_at IS 'Timestamp when exception was closed.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.resolution_reason_code IS 'Controlled resolution reason.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.closure_reason_code IS 'Controlled closure reason.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.resolved_by_user_id IS 'User who resolved the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.resolved_by_service_identity_id IS 'Service identity that resolved the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.closed_by_user_id IS 'User who closed the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.closed_by_service_identity_id IS 'Service identity that closed the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.created_by_user_id IS 'User who created the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.created_by_service_identity_id IS 'Service identity that created the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.updated_by_user_id IS 'User who last updated the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.updated_by_service_identity_id IS 'Service identity that last updated the exception.';
COMMENT ON COLUMN reconciliation.reconciliation_exceptions.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- reconciliation.settlement_comparison_records
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS reconciliation.settlement_comparison_records (

    settlement_comparison_record_id uuid DEFAULT gen_random_uuid() NOT NULL,
    reconciliation_item_id uuid NOT NULL,
    mops_transaction_record_id uuid,
    reconciliation_exception_id uuid,
    payment_confirmation_id uuid,
    provider_outcome_id uuid,
    comparison_source_type reconciliation.settlement_comparison_source_type_enum NOT NULL,
    comparison_source_ref varchar(128),
    currency_code char(3) NOT NULL,
    expected_amount numeric(18,2) NOT NULL,
    actual_amount numeric(18,2) NOT NULL,
    variance_amount numeric(18,2) NOT NULL,
    comparison_result reconciliation.settlement_comparison_result_enum NOT NULL,
    mismatch_reason_code varchar(64),
    evidence_ref varchar(256),
    evidence_hash char(64),
    compared_at timestamptz NOT NULL,
    compared_by_user_id uuid,
    compared_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_settlement_comparison_records PRIMARY KEY (settlement_comparison_record_id)
);
COMMENT ON TABLE reconciliation.settlement_comparison_records IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.settlement_comparison_record_id IS 'Canonical identifier of the settlement comparison record.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.reconciliation_item_id IS 'Reconciliation item supported by this comparison.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.mops_transaction_record_id IS 'Related MoPS or continuity-origin record.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.reconciliation_exception_id IS 'Related reconciliation exception, where applicable.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.payment_confirmation_id IS 'Related payment confirmation, where applicable.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.provider_outcome_id IS 'Related provider outcome, where applicable.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.comparison_source_type IS 'Type of settlement or financial source used.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.comparison_source_ref IS 'Source settlement, bank, batch, or report reference.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.currency_code IS 'Currency code.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.expected_amount IS 'Expected amount.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.actual_amount IS 'Actual amount from settlement evidence.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.variance_amount IS 'Actual minus expected amount.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.comparison_result IS 'Result of settlement comparison.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.mismatch_reason_code IS 'Controlled mismatch reason.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.evidence_ref IS 'Reference to settlement file, report, or evidence.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.evidence_hash IS 'Hash of settlement evidence where applicable.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.compared_at IS 'Timestamp when comparison was performed.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.compared_by_user_id IS 'User who performed comparison.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.compared_by_service_identity_id IS 'Service identity that performed comparison.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.created_by_user_id IS 'User who created the comparison.';
COMMENT ON COLUMN reconciliation.settlement_comparison_records.created_by_service_identity_id IS 'Service identity that created the comparison.';

-- ------------------------------------------------------------
-- sites.site_groups
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sites.site_groups (

    site_group_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_group_code varchar(64) NOT NULL,
    site_group_name varchar(128) NOT NULL,
    business_label varchar(64),
    description text,
    operator_entity_name varchar(128),
    timezone_name varchar(64) NOT NULL,
    default_currency_code char(3) NOT NULL,
    site_group_status sites.site_group_status_enum NOT NULL,
    public_lookup_enabled boolean DEFAULT false NOT NULL,
    default_payment_enabled boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_site_groups PRIMARY KEY (site_group_id)
);
COMMENT ON TABLE sites.site_groups IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sites.site_groups.site_group_id IS 'Canonical identifier of the site group.';
COMMENT ON COLUMN sites.site_groups.site_group_code IS 'Stable internal code for the site group.';
COMMENT ON COLUMN sites.site_groups.site_group_name IS 'Human-readable name.';
COMMENT ON COLUMN sites.site_groups.business_label IS 'Business-facing label such as PROPERTY, CLUSTER, or CAMPUS.';
COMMENT ON COLUMN sites.site_groups.description IS 'Description of the site group.';
COMMENT ON COLUMN sites.site_groups.operator_entity_name IS 'Parking operator or business entity name, where applicable.';
COMMENT ON COLUMN sites.site_groups.timezone_name IS 'IANA time zone used for local operational interpretation.';
COMMENT ON COLUMN sites.site_groups.default_currency_code IS 'Default currency for the site group.';
COMMENT ON COLUMN sites.site_groups.site_group_status IS 'Site group lifecycle status.';
COMMENT ON COLUMN sites.site_groups.public_lookup_enabled IS 'Indicates whether public Web Pay lookup is enabled for this site group.';
COMMENT ON COLUMN sites.site_groups.default_payment_enabled IS 'Indicates whether payment flow is enabled by default.';
COMMENT ON COLUMN sites.site_groups.effective_from IS 'Start of site group effectiveness.';
COMMENT ON COLUMN sites.site_groups.effective_to IS 'End of site group effectiveness.';
COMMENT ON COLUMN sites.site_groups.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sites.site_groups.created_by_user_id IS 'User who created the site group.';
COMMENT ON COLUMN sites.site_groups.created_by_service_identity_id IS 'Service identity that created the site group.';
COMMENT ON COLUMN sites.site_groups.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN sites.site_groups.updated_by_user_id IS 'User who last updated the site group.';
COMMENT ON COLUMN sites.site_groups.updated_by_service_identity_id IS 'Service identity that last updated the site group.';
COMMENT ON COLUMN sites.site_groups.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- sites.sites
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sites.sites (

    site_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_group_id uuid NOT NULL,
    site_code varchar(64) NOT NULL,
    site_name varchar(128) NOT NULL,
    site_description text,
    site_type sites.site_type_enum NOT NULL,
    timezone_name varchar(64) NOT NULL,
    address_line1 varchar(256),
    address_line2 varchar(256),
    city varchar(128),
    province varchar(128),
    country_code char(2) NOT NULL,
    lgu_code varchar(32),
    site_status sites.site_status_enum NOT NULL,
    public_lookup_enabled boolean DEFAULT false NOT NULL,
    payment_enabled boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_sites PRIMARY KEY (site_id)
);
COMMENT ON TABLE sites.sites IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sites.sites.site_id IS 'Canonical identifier of the site.';
COMMENT ON COLUMN sites.sites.site_group_id IS 'Parent site group.';
COMMENT ON COLUMN sites.sites.site_code IS 'Stable internal site code.';
COMMENT ON COLUMN sites.sites.site_name IS 'Human-readable site name.';
COMMENT ON COLUMN sites.sites.site_description IS 'Site description.';
COMMENT ON COLUMN sites.sites.site_type IS 'Site type or operational classification.';
COMMENT ON COLUMN sites.sites.timezone_name IS 'IANA time zone used for the site.';
COMMENT ON COLUMN sites.sites.address_line1 IS 'Address line 1.';
COMMENT ON COLUMN sites.sites.address_line2 IS 'Address line 2.';
COMMENT ON COLUMN sites.sites.city IS 'City or municipality.';
COMMENT ON COLUMN sites.sites.province IS 'Province or region.';
COMMENT ON COLUMN sites.sites.country_code IS 'Country code.';
COMMENT ON COLUMN sites.sites.lgu_code IS 'LGU or jurisdiction code for statutory discount policy applicability.';
COMMENT ON COLUMN sites.sites.site_status IS 'Site lifecycle status.';
COMMENT ON COLUMN sites.sites.public_lookup_enabled IS 'Indicates whether public lookup is enabled at site level.';
COMMENT ON COLUMN sites.sites.payment_enabled IS 'Indicates whether payment flow is enabled at site level.';
COMMENT ON COLUMN sites.sites.effective_from IS 'Start of site effectiveness.';
COMMENT ON COLUMN sites.sites.effective_to IS 'End of site effectiveness.';
COMMENT ON COLUMN sites.sites.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sites.sites.created_by_user_id IS 'User who created the site.';
COMMENT ON COLUMN sites.sites.created_by_service_identity_id IS 'Service identity that created the site.';
COMMENT ON COLUMN sites.sites.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN sites.sites.updated_by_user_id IS 'User who last updated the site.';
COMMENT ON COLUMN sites.sites.updated_by_service_identity_id IS 'Service identity that last updated the site.';
COMMENT ON COLUMN sites.sites.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- sites.lanes
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sites.lanes (

    lane_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_id uuid NOT NULL,
    lane_code varchar(64) NOT NULL,
    lane_name varchar(128) NOT NULL,
    lane_description text,
    lane_type sites.lane_type_enum NOT NULL,
    lane_direction sites.lane_direction_enum NOT NULL,
    lane_status sites.lane_status_enum NOT NULL,
    display_order integer,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_lanes PRIMARY KEY (lane_id)
);
COMMENT ON TABLE sites.lanes IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sites.lanes.lane_id IS 'Canonical identifier of the lane.';
COMMENT ON COLUMN sites.lanes.site_id IS 'Parent site.';
COMMENT ON COLUMN sites.lanes.lane_code IS 'Stable internal lane code.';
COMMENT ON COLUMN sites.lanes.lane_name IS 'Human-readable lane name.';
COMMENT ON COLUMN sites.lanes.lane_description IS 'Lane description.';
COMMENT ON COLUMN sites.lanes.lane_type IS 'Lane purpose or physical classification.';
COMMENT ON COLUMN sites.lanes.lane_direction IS 'Directional use of the lane.';
COMMENT ON COLUMN sites.lanes.lane_status IS 'Lane lifecycle or operational status.';
COMMENT ON COLUMN sites.lanes.display_order IS 'Optional display order in UI or reports.';
COMMENT ON COLUMN sites.lanes.effective_from IS 'Start of lane effectiveness.';
COMMENT ON COLUMN sites.lanes.effective_to IS 'End of lane effectiveness.';
COMMENT ON COLUMN sites.lanes.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sites.lanes.created_by_user_id IS 'User who created the lane.';
COMMENT ON COLUMN sites.lanes.created_by_service_identity_id IS 'Service identity that created the lane.';
COMMENT ON COLUMN sites.lanes.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN sites.lanes.updated_by_user_id IS 'User who last updated the lane.';
COMMENT ON COLUMN sites.lanes.updated_by_service_identity_id IS 'Service identity that last updated the lane.';
COMMENT ON COLUMN sites.lanes.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- sites.device_assignments
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sites.device_assignments (

    device_assignment_id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_id uuid NOT NULL,
    lane_id uuid,
    gate_device_id uuid,
    service_identity_id uuid,
    assignment_type sites.device_assignment_type_enum NOT NULL,
    assignment_status sites.device_assignment_status_enum NOT NULL,
    assignment_reason_code varchar(64),
    assigned_at timestamptz DEFAULT now() NOT NULL,
    unassigned_at timestamptz,
    assigned_by_user_id uuid,
    assigned_by_service_identity_id uuid,
    unassigned_by_user_id uuid,
    unassigned_by_service_identity_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_device_assignments PRIMARY KEY (device_assignment_id)
);
COMMENT ON TABLE sites.device_assignments IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN sites.device_assignments.device_assignment_id IS 'Canonical identifier of the device assignment.';
COMMENT ON COLUMN sites.device_assignments.site_id IS 'Site where the device is assigned.';
COMMENT ON COLUMN sites.device_assignments.lane_id IS 'Lane where the device is assigned, if lane-specific.';
COMMENT ON COLUMN sites.device_assignments.gate_device_id IS 'Gate device being assigned, where applicable.';
COMMENT ON COLUMN sites.device_assignments.service_identity_id IS 'Service or device principal associated with the assignment, where applicable.';
COMMENT ON COLUMN sites.device_assignments.assignment_type IS 'Type of assignment.';
COMMENT ON COLUMN sites.device_assignments.assignment_status IS 'Assignment lifecycle state.';
COMMENT ON COLUMN sites.device_assignments.assignment_reason_code IS 'Controlled assignment or reassignment reason.';
COMMENT ON COLUMN sites.device_assignments.assigned_at IS 'Assignment start timestamp.';
COMMENT ON COLUMN sites.device_assignments.unassigned_at IS 'Assignment end timestamp.';
COMMENT ON COLUMN sites.device_assignments.assigned_by_user_id IS 'User who assigned the device.';
COMMENT ON COLUMN sites.device_assignments.assigned_by_service_identity_id IS 'Service identity that assigned the device.';
COMMENT ON COLUMN sites.device_assignments.unassigned_by_user_id IS 'User who ended the assignment.';
COMMENT ON COLUMN sites.device_assignments.unassigned_by_service_identity_id IS 'Service identity that ended the assignment.';
COMMENT ON COLUMN sites.device_assignments.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN sites.device_assignments.created_by_user_id IS 'User who created the record.';
COMMENT ON COLUMN sites.device_assignments.created_by_service_identity_id IS 'Service identity that created the record.';
COMMENT ON COLUMN sites.device_assignments.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN sites.device_assignments.updated_by_user_id IS 'User who last updated the record.';
COMMENT ON COLUMN sites.device_assignments.updated_by_service_identity_id IS 'Service identity that last updated the record.';
COMMENT ON COLUMN sites.device_assignments.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- merchants.merchants
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS merchants.merchants (

    merchant_id uuid DEFAULT gen_random_uuid() NOT NULL,
    merchant_code varchar(64) NOT NULL,
    merchant_name varchar(256) NOT NULL,
    merchant_display_name varchar(128),
    merchant_type merchants.merchant_type_enum NOT NULL,
    merchant_status merchants.merchant_status_enum NOT NULL,
    tax_identification_number_hash char(64),
    contact_email varchar(256),
    contact_mobile_masked varchar(32),
    default_currency_code char(3) NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_merchants PRIMARY KEY (merchant_id)
);
COMMENT ON TABLE merchants.merchants IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN merchants.merchants.merchant_id IS 'Canonical identifier of the merchant.';
COMMENT ON COLUMN merchants.merchants.merchant_code IS 'Stable internal merchant code.';
COMMENT ON COLUMN merchants.merchants.merchant_name IS 'Legal or operating merchant name.';
COMMENT ON COLUMN merchants.merchants.merchant_display_name IS 'Short display name used in UI or reports.';
COMMENT ON COLUMN merchants.merchants.merchant_type IS 'Merchant classification.';
COMMENT ON COLUMN merchants.merchants.merchant_status IS 'Merchant lifecycle status.';
COMMENT ON COLUMN merchants.merchants.tax_identification_number_hash IS 'Hash of merchant TIN or equivalent identifier, where retained.';
COMMENT ON COLUMN merchants.merchants.contact_email IS 'Merchant contact email.';
COMMENT ON COLUMN merchants.merchants.contact_mobile_masked IS 'Masked merchant contact number.';
COMMENT ON COLUMN merchants.merchants.default_currency_code IS 'Default currency for merchant-sponsored benefits.';
COMMENT ON COLUMN merchants.merchants.effective_from IS 'Start of merchant effectiveness.';
COMMENT ON COLUMN merchants.merchants.effective_to IS 'End of merchant effectiveness.';
COMMENT ON COLUMN merchants.merchants.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN merchants.merchants.created_by_user_id IS 'User who created the merchant.';
COMMENT ON COLUMN merchants.merchants.created_by_service_identity_id IS 'Service identity that created the merchant.';
COMMENT ON COLUMN merchants.merchants.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN merchants.merchants.updated_by_user_id IS 'User who last updated the merchant.';
COMMENT ON COLUMN merchants.merchants.updated_by_service_identity_id IS 'Service identity that last updated the merchant.';
COMMENT ON COLUMN merchants.merchants.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- merchants.merchant_site_scopes
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS merchants.merchant_site_scopes (

    merchant_site_scope_id uuid DEFAULT gen_random_uuid() NOT NULL,
    merchant_id uuid NOT NULL,
    site_group_id uuid,
    site_id uuid,
    scope_type merchants.merchant_scope_type_enum NOT NULL,
    scope_status merchants.merchant_site_scope_status_enum NOT NULL,
    scope_reason_code varchar(64),
    allows_coupon_sponsorship boolean DEFAULT false NOT NULL,
    allows_full_waiver boolean DEFAULT false NOT NULL,
    requires_elevated_approval boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    approved_at timestamptz,
    approved_by_user_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_merchant_site_scopes PRIMARY KEY (merchant_site_scope_id)
);
COMMENT ON TABLE merchants.merchant_site_scopes IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN merchants.merchant_site_scopes.merchant_site_scope_id IS 'Canonical identifier of the merchant site scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.merchant_id IS 'Merchant to which the scope belongs.';
COMMENT ON COLUMN merchants.merchant_site_scopes.site_group_id IS 'Site group scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.site_id IS 'Site scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.scope_type IS 'Scope level.';
COMMENT ON COLUMN merchants.merchant_site_scopes.scope_status IS 'Scope lifecycle status.';
COMMENT ON COLUMN merchants.merchant_site_scopes.scope_reason_code IS 'Controlled reason for granting or changing scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.allows_coupon_sponsorship IS 'Indicates whether merchant may sponsor coupons in this scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.allows_full_waiver IS 'Indicates whether full-waiver coupons may be used in this scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.requires_elevated_approval IS 'Indicates whether merchant actions in this scope require elevated approval.';
COMMENT ON COLUMN merchants.merchant_site_scopes.effective_from IS 'Start of scope effectiveness.';
COMMENT ON COLUMN merchants.merchant_site_scopes.effective_to IS 'End of scope effectiveness.';
COMMENT ON COLUMN merchants.merchant_site_scopes.approved_at IS 'Timestamp when scope was approved.';
COMMENT ON COLUMN merchants.merchant_site_scopes.approved_by_user_id IS 'User who approved the scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN merchants.merchant_site_scopes.created_by_user_id IS 'User who created the scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.created_by_service_identity_id IS 'Service identity that created the scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN merchants.merchant_site_scopes.updated_by_user_id IS 'User who last updated the scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.updated_by_service_identity_id IS 'Service identity that last updated the scope.';
COMMENT ON COLUMN merchants.merchant_site_scopes.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- merchants.merchant_wallets
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS merchants.merchant_wallets (

    merchant_wallet_id uuid DEFAULT gen_random_uuid() NOT NULL,
    merchant_id uuid NOT NULL,
    wallet_code varchar(64) NOT NULL,
    wallet_name varchar(128) NOT NULL,
    wallet_type merchants.merchant_wallet_type_enum NOT NULL,
    wallet_status merchants.merchant_wallet_status_enum NOT NULL,
    currency_code char(3) NOT NULL,
    available_balance numeric(18,2),
    reserved_balance numeric(18,2),
    committed_balance numeric(18,2),
    external_ledger_ref varchar(128),
    allows_coupon_funding boolean DEFAULT false NOT NULL,
    allows_statutory_discount_funding boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_merchant_wallets PRIMARY KEY (merchant_wallet_id)
);
COMMENT ON TABLE merchants.merchant_wallets IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN merchants.merchant_wallets.merchant_wallet_id IS 'Canonical identifier of the merchant wallet or sponsorship funding context.';
COMMENT ON COLUMN merchants.merchant_wallets.merchant_id IS 'Merchant that owns the wallet context.';
COMMENT ON COLUMN merchants.merchant_wallets.wallet_code IS 'Stable wallet or funding context code.';
COMMENT ON COLUMN merchants.merchant_wallets.wallet_name IS 'Human-readable wallet name.';
COMMENT ON COLUMN merchants.merchant_wallets.wallet_type IS 'Wallet or funding context type.';
COMMENT ON COLUMN merchants.merchant_wallets.wallet_status IS 'Wallet lifecycle status.';
COMMENT ON COLUMN merchants.merchant_wallets.currency_code IS 'Wallet currency.';
COMMENT ON COLUMN merchants.merchant_wallets.available_balance IS 'Available commercial sponsorship balance, if locally tracked.';
COMMENT ON COLUMN merchants.merchant_wallets.reserved_balance IS 'Reserved amount for pending coupon applications, if locally tracked.';
COMMENT ON COLUMN merchants.merchant_wallets.committed_balance IS 'Committed or consumed sponsorship value, if locally tracked.';
COMMENT ON COLUMN merchants.merchant_wallets.external_ledger_ref IS 'External ledger or wallet reference, if ledger is externalized.';
COMMENT ON COLUMN merchants.merchant_wallets.allows_coupon_funding IS 'Indicates whether wallet may fund merchant coupons.';
COMMENT ON COLUMN merchants.merchant_wallets.allows_statutory_discount_funding IS 'Must be false. Merchant wallets must not fund statutory discounts.';
COMMENT ON COLUMN merchants.merchant_wallets.effective_from IS 'Start of wallet effectiveness.';
COMMENT ON COLUMN merchants.merchant_wallets.effective_to IS 'End of wallet effectiveness.';
COMMENT ON COLUMN merchants.merchant_wallets.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN merchants.merchant_wallets.created_by_user_id IS 'User who created the wallet context.';
COMMENT ON COLUMN merchants.merchant_wallets.created_by_service_identity_id IS 'Service identity that created the wallet context.';
COMMENT ON COLUMN merchants.merchant_wallets.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN merchants.merchant_wallets.updated_by_user_id IS 'User who last updated the wallet context.';
COMMENT ON COLUMN merchants.merchant_wallets.updated_by_service_identity_id IS 'Service identity that last updated the wallet context.';
COMMENT ON COLUMN merchants.merchant_wallets.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- merchants.merchant_users
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS merchants.merchant_users (

    merchant_user_id uuid DEFAULT gen_random_uuid() NOT NULL,
    merchant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    merchant_user_status merchants.merchant_user_status_enum NOT NULL,
    merchant_user_type merchants.merchant_user_type_enum NOT NULL,
    can_request_coupon boolean DEFAULT false NOT NULL,
    can_manage_coupon boolean DEFAULT false NOT NULL,
    can_view_wallet boolean DEFAULT false NOT NULL,
    can_view_reports boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    invited_at timestamptz,
    accepted_at timestamptz,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_merchant_users PRIMARY KEY (merchant_user_id)
);
COMMENT ON TABLE merchants.merchant_users IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN merchants.merchant_users.merchant_user_id IS 'Canonical identifier of the merchant-user association.';
COMMENT ON COLUMN merchants.merchant_users.merchant_id IS 'Merchant context.';
COMMENT ON COLUMN merchants.merchant_users.user_id IS 'Platform user associated with the merchant.';
COMMENT ON COLUMN merchants.merchant_users.merchant_user_status IS 'Association lifecycle status.';
COMMENT ON COLUMN merchants.merchant_users.merchant_user_type IS 'Merchant-side user classification.';
COMMENT ON COLUMN merchants.merchant_users.can_request_coupon IS 'Indicates whether the user may request coupon application.';
COMMENT ON COLUMN merchants.merchant_users.can_manage_coupon IS 'Indicates whether the user may manage coupon setup, subject to RBAC.';
COMMENT ON COLUMN merchants.merchant_users.can_view_wallet IS 'Indicates whether the user may view merchant wallet context, subject to RBAC.';
COMMENT ON COLUMN merchants.merchant_users.can_view_reports IS 'Indicates whether the user may view merchant reports, subject to RBAC.';
COMMENT ON COLUMN merchants.merchant_users.effective_from IS 'Start of association effectiveness.';
COMMENT ON COLUMN merchants.merchant_users.effective_to IS 'End of association effectiveness.';
COMMENT ON COLUMN merchants.merchant_users.invited_at IS 'Timestamp when association was invited.';
COMMENT ON COLUMN merchants.merchant_users.accepted_at IS 'Timestamp when association was accepted.';
COMMENT ON COLUMN merchants.merchant_users.revoked_at IS 'Timestamp when association was revoked.';
COMMENT ON COLUMN merchants.merchant_users.revoked_by_user_id IS 'User who revoked the association.';
COMMENT ON COLUMN merchants.merchant_users.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN merchants.merchant_users.created_by_user_id IS 'User who created the association.';
COMMENT ON COLUMN merchants.merchant_users.created_by_service_identity_id IS 'Service identity that created the association.';
COMMENT ON COLUMN merchants.merchant_users.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN merchants.merchant_users.updated_by_user_id IS 'User who last updated the association.';
COMMENT ON COLUMN merchants.merchant_users.updated_by_service_identity_id IS 'Service identity that last updated the association.';
COMMENT ON COLUMN merchants.merchant_users.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- identity.users
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS identity.users (

    user_id uuid DEFAULT gen_random_uuid() NOT NULL,
    username varchar(128) NOT NULL,
    email varchar(256),
    email_normalized varchar(256),
    display_name varchar(128) NOT NULL,
    mobile_number_masked varchar(32),
    user_type identity.user_type_enum NOT NULL,
    user_status identity.user_status_enum NOT NULL,
    last_login_at timestamptz,
    locked_at timestamptz,
    suspended_at timestamptz,
    retired_at timestamptz,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (user_id)
);
COMMENT ON TABLE identity.users IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN identity.users.user_id IS 'Canonical identifier of the human user.';
COMMENT ON COLUMN identity.users.username IS 'Stable login or platform username.';
COMMENT ON COLUMN identity.users.email IS 'User email address.';
COMMENT ON COLUMN identity.users.email_normalized IS 'Normalized email for uniqueness and lookup.';
COMMENT ON COLUMN identity.users.display_name IS 'Human-readable display name.';
COMMENT ON COLUMN identity.users.mobile_number_masked IS 'Masked mobile number where retained.';
COMMENT ON COLUMN identity.users.user_type IS 'User classification.';
COMMENT ON COLUMN identity.users.user_status IS 'User lifecycle status.';
COMMENT ON COLUMN identity.users.last_login_at IS 'Last successful login timestamp.';
COMMENT ON COLUMN identity.users.locked_at IS 'Timestamp when user was locked, if applicable.';
COMMENT ON COLUMN identity.users.suspended_at IS 'Timestamp when user was suspended, if applicable.';
COMMENT ON COLUMN identity.users.retired_at IS 'Timestamp when user was retired.';
COMMENT ON COLUMN identity.users.effective_from IS 'Start of user effectiveness.';
COMMENT ON COLUMN identity.users.effective_to IS 'End of user effectiveness.';
COMMENT ON COLUMN identity.users.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN identity.users.created_by_user_id IS 'User who created the user record.';
COMMENT ON COLUMN identity.users.created_by_service_identity_id IS 'Service identity that created the user record.';
COMMENT ON COLUMN identity.users.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN identity.users.updated_by_user_id IS 'User who last updated the user record.';
COMMENT ON COLUMN identity.users.updated_by_service_identity_id IS 'Service identity that last updated the user record.';
COMMENT ON COLUMN identity.users.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- identity.roles
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS identity.roles (

    role_id uuid DEFAULT gen_random_uuid() NOT NULL,
    role_code varchar(64) NOT NULL,
    role_name varchar(128) NOT NULL,
    role_description text,
    role_type identity.role_type_enum NOT NULL,
    role_status identity.role_status_enum NOT NULL,
    is_privileged boolean DEFAULT false NOT NULL,
    requires_elevated_approval boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_roles PRIMARY KEY (role_id)
);
COMMENT ON TABLE identity.roles IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN identity.roles.role_id IS 'Canonical identifier of the role.';
COMMENT ON COLUMN identity.roles.role_code IS 'Stable role code.';
COMMENT ON COLUMN identity.roles.role_name IS 'Human-readable role name.';
COMMENT ON COLUMN identity.roles.role_description IS 'Description of the role.';
COMMENT ON COLUMN identity.roles.role_type IS 'Role classification.';
COMMENT ON COLUMN identity.roles.role_status IS 'Role lifecycle status.';
COMMENT ON COLUMN identity.roles.is_privileged IS 'Indicates whether the role grants privileged or sensitive access.';
COMMENT ON COLUMN identity.roles.requires_elevated_approval IS 'Indicates whether assignment requires elevated approval.';
COMMENT ON COLUMN identity.roles.effective_from IS 'Start of role effectiveness.';
COMMENT ON COLUMN identity.roles.effective_to IS 'End of role effectiveness.';
COMMENT ON COLUMN identity.roles.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN identity.roles.created_by_user_id IS 'User who created the role.';
COMMENT ON COLUMN identity.roles.created_by_service_identity_id IS 'Service identity that created the role.';
COMMENT ON COLUMN identity.roles.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN identity.roles.updated_by_user_id IS 'User who last updated the role.';
COMMENT ON COLUMN identity.roles.updated_by_service_identity_id IS 'Service identity that last updated the role.';
COMMENT ON COLUMN identity.roles.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- identity.permissions
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS identity.permissions (

    permission_id uuid DEFAULT gen_random_uuid() NOT NULL,
    permission_code varchar(96) NOT NULL,
    permission_name varchar(128) NOT NULL,
    permission_description text,
    permission_domain varchar(64) NOT NULL,
    permission_action varchar(64) NOT NULL,
    permission_status identity.permission_status_enum NOT NULL,
    is_sensitive boolean DEFAULT false NOT NULL,
    requires_audit boolean DEFAULT false NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_permissions PRIMARY KEY (permission_id)
);
COMMENT ON TABLE identity.permissions IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN identity.permissions.permission_id IS 'Canonical identifier of the permission.';
COMMENT ON COLUMN identity.permissions.permission_code IS 'Stable permission code.';
COMMENT ON COLUMN identity.permissions.permission_name IS 'Human-readable permission name.';
COMMENT ON COLUMN identity.permissions.permission_description IS 'Description of the permission.';
COMMENT ON COLUMN identity.permissions.permission_domain IS 'Domain or schema to which the permission belongs.';
COMMENT ON COLUMN identity.permissions.permission_action IS 'Action category.';
COMMENT ON COLUMN identity.permissions.permission_status IS 'Permission lifecycle status.';
COMMENT ON COLUMN identity.permissions.is_sensitive IS 'Indicates whether the permission grants sensitive access.';
COMMENT ON COLUMN identity.permissions.requires_audit IS 'Indicates whether use of the permission should be auditable.';
COMMENT ON COLUMN identity.permissions.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN identity.permissions.created_by_user_id IS 'User who created the permission.';
COMMENT ON COLUMN identity.permissions.created_by_service_identity_id IS 'Service identity that created the permission.';
COMMENT ON COLUMN identity.permissions.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN identity.permissions.updated_by_user_id IS 'User who last updated the permission.';
COMMENT ON COLUMN identity.permissions.updated_by_service_identity_id IS 'Service identity that last updated the permission.';
COMMENT ON COLUMN identity.permissions.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- identity.user_roles
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS identity.user_roles (

    user_role_id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    role_id uuid NOT NULL,
    assignment_status identity.user_role_assignment_status_enum NOT NULL,
    assignment_reason_code varchar(64),
    assigned_at timestamptz DEFAULT now() NOT NULL,
    assigned_by_user_id uuid,
    assigned_by_service_identity_id uuid,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    revocation_reason_code varchar(64),
    last_reviewed_at timestamptz,
    last_reviewed_by_user_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_user_roles PRIMARY KEY (user_role_id)
);
COMMENT ON TABLE identity.user_roles IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN identity.user_roles.user_role_id IS 'Canonical identifier of the user-role assignment.';
COMMENT ON COLUMN identity.user_roles.user_id IS 'User receiving the role.';
COMMENT ON COLUMN identity.user_roles.role_id IS 'Role assigned to the user.';
COMMENT ON COLUMN identity.user_roles.assignment_status IS 'Assignment lifecycle status.';
COMMENT ON COLUMN identity.user_roles.assignment_reason_code IS 'Controlled reason for assignment.';
COMMENT ON COLUMN identity.user_roles.assigned_at IS 'Timestamp when role assignment became effective or was created.';
COMMENT ON COLUMN identity.user_roles.assigned_by_user_id IS 'User who assigned the role.';
COMMENT ON COLUMN identity.user_roles.assigned_by_service_identity_id IS 'Service identity that assigned the role.';
COMMENT ON COLUMN identity.user_roles.effective_from IS 'Start of role assignment effectiveness.';
COMMENT ON COLUMN identity.user_roles.effective_to IS 'End of role assignment effectiveness.';
COMMENT ON COLUMN identity.user_roles.revoked_at IS 'Timestamp when assignment was revoked.';
COMMENT ON COLUMN identity.user_roles.revoked_by_user_id IS 'User who revoked the assignment.';
COMMENT ON COLUMN identity.user_roles.revoked_by_service_identity_id IS 'Service identity that revoked the assignment.';
COMMENT ON COLUMN identity.user_roles.revocation_reason_code IS 'Controlled reason for revocation.';
COMMENT ON COLUMN identity.user_roles.last_reviewed_at IS 'Last access-review timestamp.';
COMMENT ON COLUMN identity.user_roles.last_reviewed_by_user_id IS 'User who reviewed the assignment.';
COMMENT ON COLUMN identity.user_roles.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN identity.user_roles.created_by_user_id IS 'User who created the assignment.';
COMMENT ON COLUMN identity.user_roles.created_by_service_identity_id IS 'Service identity that created the assignment.';
COMMENT ON COLUMN identity.user_roles.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN identity.user_roles.updated_by_user_id IS 'User who last updated the assignment.';
COMMENT ON COLUMN identity.user_roles.updated_by_service_identity_id IS 'Service identity that last updated the assignment.';
COMMENT ON COLUMN identity.user_roles.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- identity.role_permissions
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS identity.role_permissions (

    role_permission_id uuid DEFAULT gen_random_uuid() NOT NULL,
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL,
    binding_status identity.role_permission_binding_status_enum NOT NULL,
    binding_reason_code varchar(64),
    assigned_at timestamptz DEFAULT now() NOT NULL,
    assigned_by_user_id uuid,
    assigned_by_service_identity_id uuid,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    revocation_reason_code varchar(64),
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_role_permissions PRIMARY KEY (role_permission_id)
);
COMMENT ON TABLE identity.role_permissions IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN identity.role_permissions.role_permission_id IS 'Canonical identifier of the role-permission binding.';
COMMENT ON COLUMN identity.role_permissions.role_id IS 'Role receiving the permission.';
COMMENT ON COLUMN identity.role_permissions.permission_id IS 'Permission assigned to the role.';
COMMENT ON COLUMN identity.role_permissions.binding_status IS 'Binding lifecycle status.';
COMMENT ON COLUMN identity.role_permissions.binding_reason_code IS 'Controlled reason for binding.';
COMMENT ON COLUMN identity.role_permissions.assigned_at IS 'Timestamp when permission binding was assigned.';
COMMENT ON COLUMN identity.role_permissions.assigned_by_user_id IS 'User who assigned the permission to the role.';
COMMENT ON COLUMN identity.role_permissions.assigned_by_service_identity_id IS 'Service identity that assigned the permission.';
COMMENT ON COLUMN identity.role_permissions.effective_from IS 'Start of binding effectiveness.';
COMMENT ON COLUMN identity.role_permissions.effective_to IS 'End of binding effectiveness.';
COMMENT ON COLUMN identity.role_permissions.revoked_at IS 'Timestamp when permission binding was revoked.';
COMMENT ON COLUMN identity.role_permissions.revoked_by_user_id IS 'User who revoked the binding.';
COMMENT ON COLUMN identity.role_permissions.revoked_by_service_identity_id IS 'Service identity that revoked the binding.';
COMMENT ON COLUMN identity.role_permissions.revocation_reason_code IS 'Controlled reason for revocation.';
COMMENT ON COLUMN identity.role_permissions.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN identity.role_permissions.created_by_user_id IS 'User who created the binding.';
COMMENT ON COLUMN identity.role_permissions.created_by_service_identity_id IS 'Service identity that created the binding.';
COMMENT ON COLUMN identity.role_permissions.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN identity.role_permissions.updated_by_user_id IS 'User who last updated the binding.';
COMMENT ON COLUMN identity.role_permissions.updated_by_service_identity_id IS 'Service identity that last updated the binding.';
COMMENT ON COLUMN identity.role_permissions.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- identity.service_identities
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS identity.service_identities (

    service_identity_id uuid DEFAULT gen_random_uuid() NOT NULL,
    service_identity_code varchar(64) NOT NULL,
    service_identity_name varchar(128) NOT NULL,
    identity_type identity.service_identity_type_enum NOT NULL,
    identity_status identity.service_identity_status_enum NOT NULL,
    owning_service_name varchar(128),
    credential_reference varchar(256),
    credential_type identity.service_credential_type_enum,
    credential_expires_at timestamptz,
    last_rotated_at timestamptz,
    last_authenticated_at timestamptz,
    revoked_at timestamptz,
    revocation_reason_code varchar(64),
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_service_identities PRIMARY KEY (service_identity_id)
);
COMMENT ON TABLE identity.service_identities IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN identity.service_identities.service_identity_id IS 'Canonical identifier of the service identity.';
COMMENT ON COLUMN identity.service_identities.service_identity_code IS 'Stable internal service identity code.';
COMMENT ON COLUMN identity.service_identities.service_identity_name IS 'Human-readable service identity name.';
COMMENT ON COLUMN identity.service_identities.identity_type IS 'Type of non-human principal.';
COMMENT ON COLUMN identity.service_identities.identity_status IS 'Service identity lifecycle status.';
COMMENT ON COLUMN identity.service_identities.owning_service_name IS 'Owning service, worker, adapter, or component name.';
COMMENT ON COLUMN identity.service_identities.credential_reference IS 'Reference to secret, certificate, key vault entry, or credential profile.';
COMMENT ON COLUMN identity.service_identities.credential_type IS 'Credential type used by the service identity.';
COMMENT ON COLUMN identity.service_identities.credential_expires_at IS 'Credential expiry timestamp, where applicable.';
COMMENT ON COLUMN identity.service_identities.last_rotated_at IS 'Last credential rotation timestamp.';
COMMENT ON COLUMN identity.service_identities.last_authenticated_at IS 'Last successful authentication timestamp.';
COMMENT ON COLUMN identity.service_identities.revoked_at IS 'Timestamp when identity was revoked.';
COMMENT ON COLUMN identity.service_identities.revocation_reason_code IS 'Controlled revocation reason.';
COMMENT ON COLUMN identity.service_identities.effective_from IS 'Start of service identity effectiveness.';
COMMENT ON COLUMN identity.service_identities.effective_to IS 'End of service identity effectiveness.';
COMMENT ON COLUMN identity.service_identities.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN identity.service_identities.created_by_user_id IS 'User who created the service identity.';
COMMENT ON COLUMN identity.service_identities.created_by_service_identity_id IS 'Service identity that created this service identity.';
COMMENT ON COLUMN identity.service_identities.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN identity.service_identities.updated_by_user_id IS 'User who last updated the service identity.';
COMMENT ON COLUMN identity.service_identities.updated_by_service_identity_id IS 'Service identity that last updated this service identity.';
COMMENT ON COLUMN identity.service_identities.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- audit.audit_events
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.audit_events (

    audit_event_id uuid DEFAULT gen_random_uuid() NOT NULL,
    event_type varchar(96) NOT NULL,
    event_category audit.audit_event_category_enum NOT NULL,
    event_result audit.audit_event_result_enum NOT NULL,
    event_reason_code varchar(64),
    target_entity_type varchar(64),
    target_entity_id uuid,
    related_entity_type varchar(64),
    related_entity_id uuid,
    source_schema varchar(64),
    source_service_name varchar(128),
    source_channel varchar(64),
    actor_user_id uuid,
    actor_service_identity_id uuid,
    actor_ip_hash char(64),
    actor_user_agent_hash char(64),
    summary varchar(256),
    details_ref varchar(256),
    details_hash char(64),
    occurred_at timestamptz NOT NULL,
    recorded_at timestamptz DEFAULT now() NOT NULL,
    correlation_id uuid,
    causation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_audit_events PRIMARY KEY (audit_event_id)
);
COMMENT ON TABLE audit.audit_events IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN audit.audit_events.audit_event_id IS 'Canonical identifier of the audit event.';
COMMENT ON COLUMN audit.audit_events.event_type IS 'Controlled audit event type.';
COMMENT ON COLUMN audit.audit_events.event_category IS 'Audit event category.';
COMMENT ON COLUMN audit.audit_events.event_result IS 'Event result classification.';
COMMENT ON COLUMN audit.audit_events.event_reason_code IS 'Controlled reason code explaining result.';
COMMENT ON COLUMN audit.audit_events.target_entity_type IS 'Type of affected domain entity.';
COMMENT ON COLUMN audit.audit_events.target_entity_id IS 'Identifier of affected domain entity.';
COMMENT ON COLUMN audit.audit_events.related_entity_type IS 'Type of related domain entity, where applicable.';
COMMENT ON COLUMN audit.audit_events.related_entity_id IS 'Identifier of related domain entity.';
COMMENT ON COLUMN audit.audit_events.source_schema IS 'Source schema or domain that produced the audit event.';
COMMENT ON COLUMN audit.audit_events.source_service_name IS 'Source service that produced the event.';
COMMENT ON COLUMN audit.audit_events.source_channel IS 'Source channel, such as Web Pay, API, worker, gate, or admin UI.';
COMMENT ON COLUMN audit.audit_events.actor_user_id IS 'Human actor, where applicable.';
COMMENT ON COLUMN audit.audit_events.actor_service_identity_id IS 'Service, adapter, job, or device actor, where applicable.';
COMMENT ON COLUMN audit.audit_events.actor_ip_hash IS 'Hash of actor IP where retained.';
COMMENT ON COLUMN audit.audit_events.actor_user_agent_hash IS 'Hash of user agent where retained.';
COMMENT ON COLUMN audit.audit_events.summary IS 'Short audit summary.';
COMMENT ON COLUMN audit.audit_events.details_ref IS 'Reference to structured audit details if stored separately.';
COMMENT ON COLUMN audit.audit_events.details_hash IS 'Hash of details payload where retained.';
COMMENT ON COLUMN audit.audit_events.occurred_at IS 'Timestamp when audited event occurred.';
COMMENT ON COLUMN audit.audit_events.recorded_at IS 'Timestamp when audit event was recorded.';
COMMENT ON COLUMN audit.audit_events.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN audit.audit_events.causation_id IS 'Causation identifier where applicable.';
COMMENT ON COLUMN audit.audit_events.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN audit.audit_events.created_by_service_identity_id IS 'Service identity that wrote the audit event.';

-- ------------------------------------------------------------
-- audit.audit_trail_entries
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.audit_trail_entries (

    audit_trail_entry_id uuid DEFAULT gen_random_uuid() NOT NULL,
    audit_event_id uuid,
    change_type audit.audit_change_type_enum NOT NULL,
    target_entity_type varchar(64) NOT NULL,
    target_entity_id uuid NOT NULL,
    field_name varchar(128),
    before_value_hash char(64),
    after_value_hash char(64),
    before_value_redacted text,
    after_value_redacted text,
    change_summary varchar(256),
    change_reason_code varchar(64),
    changed_at timestamptz NOT NULL,
    changed_by_user_id uuid,
    changed_by_service_identity_id uuid,
    approval_reference_type varchar(64),
    approval_reference_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_audit_trail_entries PRIMARY KEY (audit_trail_entry_id)
);
COMMENT ON TABLE audit.audit_trail_entries IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN audit.audit_trail_entries.audit_trail_entry_id IS 'Canonical identifier of the audit trail entry.';
COMMENT ON COLUMN audit.audit_trail_entries.audit_event_id IS 'Parent audit event, where applicable.';
COMMENT ON COLUMN audit.audit_trail_entries.change_type IS 'Type of change recorded.';
COMMENT ON COLUMN audit.audit_trail_entries.target_entity_type IS 'Type of changed entity.';
COMMENT ON COLUMN audit.audit_trail_entries.target_entity_id IS 'Identifier of changed entity.';
COMMENT ON COLUMN audit.audit_trail_entries.field_name IS 'Field that changed, if field-level trail is used.';
COMMENT ON COLUMN audit.audit_trail_entries.before_value_hash IS 'Hash of previous value where value is sensitive or large.';
COMMENT ON COLUMN audit.audit_trail_entries.after_value_hash IS 'Hash of new value where value is sensitive or large.';
COMMENT ON COLUMN audit.audit_trail_entries.before_value_redacted IS 'Redacted before value, where allowed.';
COMMENT ON COLUMN audit.audit_trail_entries.after_value_redacted IS 'Redacted after value, where allowed.';
COMMENT ON COLUMN audit.audit_trail_entries.change_summary IS 'Short summary of change.';
COMMENT ON COLUMN audit.audit_trail_entries.change_reason_code IS 'Controlled reason for change.';
COMMENT ON COLUMN audit.audit_trail_entries.changed_at IS 'Timestamp when change occurred.';
COMMENT ON COLUMN audit.audit_trail_entries.changed_by_user_id IS 'Human actor responsible for change.';
COMMENT ON COLUMN audit.audit_trail_entries.changed_by_service_identity_id IS 'Service actor responsible for change.';
COMMENT ON COLUMN audit.audit_trail_entries.approval_reference_type IS 'Approval entity type, where change was approved.';
COMMENT ON COLUMN audit.audit_trail_entries.approval_reference_id IS 'Approval entity ID, where change was approved.';
COMMENT ON COLUMN audit.audit_trail_entries.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN audit.audit_trail_entries.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN audit.audit_trail_entries.created_by_service_identity_id IS 'Service identity that wrote the audit trail entry.';

-- ------------------------------------------------------------
-- audit.security_events
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.security_events (

    security_event_id uuid DEFAULT gen_random_uuid() NOT NULL,
    audit_event_id uuid,
    security_event_type varchar(96) NOT NULL,
    security_event_category audit.security_event_category_enum NOT NULL,
    security_severity audit.security_severity_enum NOT NULL,
    security_event_status audit.security_event_status_enum NOT NULL,
    result audit.security_event_result_enum NOT NULL,
    reason_code varchar(64),
    target_entity_type varchar(64),
    target_entity_id uuid,
    actor_user_id uuid,
    actor_service_identity_id uuid,
    source_ip_hash char(64),
    user_agent_hash char(64),
    request_fingerprint_hash char(64),
    incident_record_id uuid,
    detected_at timestamptz NOT NULL,
    recorded_at timestamptz DEFAULT now() NOT NULL,
    resolved_at timestamptz,
    resolved_by_user_id uuid,
    resolution_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_security_events PRIMARY KEY (security_event_id)
);
COMMENT ON TABLE audit.security_events IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN audit.security_events.security_event_id IS 'Canonical identifier of the security event.';
COMMENT ON COLUMN audit.security_events.audit_event_id IS 'Related audit event, where applicable.';
COMMENT ON COLUMN audit.security_events.security_event_type IS 'Controlled security event type.';
COMMENT ON COLUMN audit.security_events.security_event_category IS 'Security event category.';
COMMENT ON COLUMN audit.security_events.security_severity IS 'Severity of security event.';
COMMENT ON COLUMN audit.security_events.security_event_status IS 'Security event lifecycle or handling state.';
COMMENT ON COLUMN audit.security_events.result IS 'Result classification.';
COMMENT ON COLUMN audit.security_events.reason_code IS 'Controlled security reason.';
COMMENT ON COLUMN audit.security_events.target_entity_type IS 'Target entity type.';
COMMENT ON COLUMN audit.security_events.target_entity_id IS 'Target entity ID.';
COMMENT ON COLUMN audit.security_events.actor_user_id IS 'Human actor involved.';
COMMENT ON COLUMN audit.security_events.actor_service_identity_id IS 'Service or device actor involved.';
COMMENT ON COLUMN audit.security_events.source_ip_hash IS 'Hash of source IP where retained.';
COMMENT ON COLUMN audit.security_events.user_agent_hash IS 'Hash of user agent where retained.';
COMMENT ON COLUMN audit.security_events.request_fingerprint_hash IS 'Hash of request fingerprint where retained.';
COMMENT ON COLUMN audit.security_events.incident_record_id IS 'Related incident, where material.';
COMMENT ON COLUMN audit.security_events.detected_at IS 'Timestamp when event was detected.';
COMMENT ON COLUMN audit.security_events.recorded_at IS 'Timestamp when event was recorded.';
COMMENT ON COLUMN audit.security_events.resolved_at IS 'Timestamp when security event was resolved or closed.';
COMMENT ON COLUMN audit.security_events.resolved_by_user_id IS 'User who resolved or reviewed the event.';
COMMENT ON COLUMN audit.security_events.resolution_reason_code IS 'Controlled resolution reason.';
COMMENT ON COLUMN audit.security_events.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN audit.security_events.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN audit.security_events.created_by_service_identity_id IS 'Service identity that wrote the security event.';
COMMENT ON COLUMN audit.security_events.row_version IS 'Optimistic concurrency version if status can change.';

-- ------------------------------------------------------------
-- audit.evidence_links
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.evidence_links (

    evidence_link_id uuid DEFAULT gen_random_uuid() NOT NULL,
    audit_event_id uuid,
    security_event_id uuid,
    target_entity_type varchar(64),
    target_entity_id uuid,
    evidence_type audit.evidence_type_enum NOT NULL,
    evidence_storage_type audit.evidence_storage_type_enum NOT NULL,
    evidence_storage_ref varchar(256),
    evidence_hash char(64),
    access_classification audit.evidence_access_classification_enum NOT NULL,
    retention_policy_code varchar(64) NOT NULL,
    retention_expires_at timestamptz,
    redaction_status audit.evidence_redaction_status_enum NOT NULL,
    link_status audit.evidence_link_status_enum NOT NULL,
    linked_at timestamptz DEFAULT now() NOT NULL,
    linked_by_user_id uuid,
    linked_by_service_identity_id uuid,
    purged_at timestamptz,
    purged_by_user_id uuid,
    purged_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_evidence_links PRIMARY KEY (evidence_link_id)
);
COMMENT ON TABLE audit.evidence_links IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN audit.evidence_links.evidence_link_id IS 'Canonical identifier of the evidence link.';
COMMENT ON COLUMN audit.evidence_links.audit_event_id IS 'Audit event supported by the evidence.';
COMMENT ON COLUMN audit.evidence_links.security_event_id IS 'Security event supported by the evidence.';
COMMENT ON COLUMN audit.evidence_links.target_entity_type IS 'Domain entity type supported by the evidence.';
COMMENT ON COLUMN audit.evidence_links.target_entity_id IS 'Domain entity identifier supported by the evidence.';
COMMENT ON COLUMN audit.evidence_links.evidence_type IS 'Type of evidence.';
COMMENT ON COLUMN audit.evidence_links.evidence_storage_type IS 'Storage mechanism or reference type.';
COMMENT ON COLUMN audit.evidence_links.evidence_storage_ref IS 'Object key, evidence vault reference, URI reference, or controlled storage reference.';
COMMENT ON COLUMN audit.evidence_links.evidence_hash IS 'Hash of evidence content where retained.';
COMMENT ON COLUMN audit.evidence_links.access_classification IS 'Access classification.';
COMMENT ON COLUMN audit.evidence_links.retention_policy_code IS 'Retention policy applied to evidence.';
COMMENT ON COLUMN audit.evidence_links.retention_expires_at IS 'Timestamp when evidence becomes eligible for purge or redaction.';
COMMENT ON COLUMN audit.evidence_links.redaction_status IS 'Redaction or minimization state.';
COMMENT ON COLUMN audit.evidence_links.link_status IS 'Evidence link lifecycle state.';
COMMENT ON COLUMN audit.evidence_links.linked_at IS 'Timestamp when evidence was linked.';
COMMENT ON COLUMN audit.evidence_links.linked_by_user_id IS 'User who linked the evidence.';
COMMENT ON COLUMN audit.evidence_links.linked_by_service_identity_id IS 'Service identity that linked the evidence.';
COMMENT ON COLUMN audit.evidence_links.purged_at IS 'Timestamp when evidence payload was purged, if applicable.';
COMMENT ON COLUMN audit.evidence_links.purged_by_user_id IS 'User who purged the evidence.';
COMMENT ON COLUMN audit.evidence_links.purged_by_service_identity_id IS 'Service identity that purged the evidence.';
COMMENT ON COLUMN audit.evidence_links.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN audit.evidence_links.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN audit.evidence_links.created_by_user_id IS 'User who created the evidence link.';
COMMENT ON COLUMN audit.evidence_links.created_by_service_identity_id IS 'Service identity that created the evidence link.';
COMMENT ON COLUMN audit.evidence_links.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN audit.evidence_links.updated_by_user_id IS 'User who last updated the evidence link.';
COMMENT ON COLUMN audit.evidence_links.updated_by_service_identity_id IS 'Service identity that last updated the evidence link.';
COMMENT ON COLUMN audit.evidence_links.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- integration.vendor_systems
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS integration.vendor_systems (

    vendor_system_id uuid DEFAULT gen_random_uuid() NOT NULL,
    vendor_code varchar(64) NOT NULL,
    vendor_name varchar(128) NOT NULL,
    vendor_system_type integration.vendor_system_type_enum NOT NULL,
    vendor_system_status integration.vendor_system_status_enum NOT NULL,
    environment_code varchar(32) NOT NULL,
    base_url_ref varchar(256),
    api_version varchar(64),
    owner_team varchar(128),
    support_contact_ref varchar(128),
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_vendor_systems PRIMARY KEY (vendor_system_id)
);
COMMENT ON TABLE integration.vendor_systems IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN integration.vendor_systems.vendor_system_id IS 'Canonical identifier of the vendor system.';
COMMENT ON COLUMN integration.vendor_systems.vendor_code IS 'Stable internal vendor code.';
COMMENT ON COLUMN integration.vendor_systems.vendor_name IS 'Human-readable vendor or system name.';
COMMENT ON COLUMN integration.vendor_systems.vendor_system_type IS 'Vendor system classification.';
COMMENT ON COLUMN integration.vendor_systems.vendor_system_status IS 'Vendor system lifecycle status.';
COMMENT ON COLUMN integration.vendor_systems.environment_code IS 'Environment where vendor profile applies.';
COMMENT ON COLUMN integration.vendor_systems.base_url_ref IS 'Reference or non-secret base URL configuration.';
COMMENT ON COLUMN integration.vendor_systems.api_version IS 'Vendor API version used.';
COMMENT ON COLUMN integration.vendor_systems.owner_team IS 'Internal owner team or responsible unit.';
COMMENT ON COLUMN integration.vendor_systems.support_contact_ref IS 'Support contact or vendor support reference.';
COMMENT ON COLUMN integration.vendor_systems.effective_from IS 'Start of vendor system effectiveness.';
COMMENT ON COLUMN integration.vendor_systems.effective_to IS 'End of vendor system effectiveness.';
COMMENT ON COLUMN integration.vendor_systems.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN integration.vendor_systems.created_by_user_id IS 'User who created the vendor system record.';
COMMENT ON COLUMN integration.vendor_systems.created_by_service_identity_id IS 'Service identity that created the vendor system record.';
COMMENT ON COLUMN integration.vendor_systems.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN integration.vendor_systems.updated_by_user_id IS 'User who last updated the vendor system record.';
COMMENT ON COLUMN integration.vendor_systems.updated_by_service_identity_id IS 'Service identity that last updated the vendor system record.';
COMMENT ON COLUMN integration.vendor_systems.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- integration.vendor_endpoints
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS integration.vendor_endpoints (

    vendor_endpoint_id uuid DEFAULT gen_random_uuid() NOT NULL,
    vendor_system_id uuid NOT NULL,
    endpoint_code varchar(96) NOT NULL,
    endpoint_name varchar(128) NOT NULL,
    endpoint_description text,
    endpoint_type integration.vendor_endpoint_type_enum NOT NULL,
    http_method integration.http_method_enum,
    path_template varchar(512),
    operation_ref varchar(128),
    credential_reference_id uuid,
    timeout_policy_code varchar(64),
    retry_policy_code varchar(64),
    rate_limit_policy_code varchar(64),
    endpoint_status integration.vendor_endpoint_status_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_vendor_endpoints PRIMARY KEY (vendor_endpoint_id)
);
COMMENT ON TABLE integration.vendor_endpoints IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN integration.vendor_endpoints.vendor_endpoint_id IS 'Canonical identifier of the vendor endpoint.';
COMMENT ON COLUMN integration.vendor_endpoints.vendor_system_id IS 'Vendor system that exposes the endpoint.';
COMMENT ON COLUMN integration.vendor_endpoints.endpoint_code IS 'Stable internal endpoint operation code.';
COMMENT ON COLUMN integration.vendor_endpoints.endpoint_name IS 'Human-readable endpoint name.';
COMMENT ON COLUMN integration.vendor_endpoints.endpoint_description IS 'Endpoint description.';
COMMENT ON COLUMN integration.vendor_endpoints.endpoint_type IS 'Endpoint classification.';
COMMENT ON COLUMN integration.vendor_endpoints.http_method IS 'HTTP method where REST-based.';
COMMENT ON COLUMN integration.vendor_endpoints.path_template IS 'Endpoint path template.';
COMMENT ON COLUMN integration.vendor_endpoints.operation_ref IS 'Vendor SDK, SOAP, OpenAPI, or operation reference.';
COMMENT ON COLUMN integration.vendor_endpoints.credential_reference_id IS 'Credential reference used by the endpoint.';
COMMENT ON COLUMN integration.vendor_endpoints.timeout_policy_code IS 'Timeout policy code.';
COMMENT ON COLUMN integration.vendor_endpoints.retry_policy_code IS 'Retry policy code.';
COMMENT ON COLUMN integration.vendor_endpoints.rate_limit_policy_code IS 'Rate-limit policy code.';
COMMENT ON COLUMN integration.vendor_endpoints.endpoint_status IS 'Endpoint lifecycle status.';
COMMENT ON COLUMN integration.vendor_endpoints.effective_from IS 'Start of endpoint effectiveness.';
COMMENT ON COLUMN integration.vendor_endpoints.effective_to IS 'End of endpoint effectiveness.';
COMMENT ON COLUMN integration.vendor_endpoints.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN integration.vendor_endpoints.created_by_user_id IS 'User who created the endpoint.';
COMMENT ON COLUMN integration.vendor_endpoints.created_by_service_identity_id IS 'Service identity that created the endpoint.';
COMMENT ON COLUMN integration.vendor_endpoints.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN integration.vendor_endpoints.updated_by_user_id IS 'User who last updated the endpoint.';
COMMENT ON COLUMN integration.vendor_endpoints.updated_by_service_identity_id IS 'Service identity that last updated the endpoint.';
COMMENT ON COLUMN integration.vendor_endpoints.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- integration.adapter_mappings
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS integration.adapter_mappings (

    adapter_mapping_id uuid DEFAULT gen_random_uuid() NOT NULL,
    vendor_system_id uuid NOT NULL,
    mapping_type integration.adapter_mapping_type_enum NOT NULL,
    site_group_id uuid,
    site_id uuid,
    lane_id uuid,
    gate_device_id uuid,
    payment_rail_id uuid,
    vendor_object_type varchar(64) NOT NULL,
    vendor_object_ref varchar(128) NOT NULL,
    vendor_object_name varchar(128),
    mapping_status integration.adapter_mapping_status_enum NOT NULL,
    mapping_confidence integration.adapter_mapping_confidence_enum,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_adapter_mappings PRIMARY KEY (adapter_mapping_id)
);
COMMENT ON TABLE integration.adapter_mappings IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN integration.adapter_mappings.adapter_mapping_id IS 'Canonical identifier of the adapter mapping.';
COMMENT ON COLUMN integration.adapter_mappings.vendor_system_id IS 'Vendor system to which the mapping applies.';
COMMENT ON COLUMN integration.adapter_mappings.mapping_type IS 'Type of mapping.';
COMMENT ON COLUMN integration.adapter_mappings.site_group_id IS 'ExitPass site group being mapped.';
COMMENT ON COLUMN integration.adapter_mappings.site_id IS 'ExitPass site being mapped.';
COMMENT ON COLUMN integration.adapter_mappings.lane_id IS 'ExitPass lane being mapped.';
COMMENT ON COLUMN integration.adapter_mappings.gate_device_id IS 'ExitPass gate device being mapped.';
COMMENT ON COLUMN integration.adapter_mappings.payment_rail_id IS 'ExitPass payment rail being mapped.';
COMMENT ON COLUMN integration.adapter_mappings.vendor_object_type IS 'Vendor object type.';
COMMENT ON COLUMN integration.adapter_mappings.vendor_object_ref IS 'Vendor object identifier.';
COMMENT ON COLUMN integration.adapter_mappings.vendor_object_name IS 'Vendor object display name.';
COMMENT ON COLUMN integration.adapter_mappings.mapping_status IS 'Mapping lifecycle status.';
COMMENT ON COLUMN integration.adapter_mappings.mapping_confidence IS 'Confidence or source of mapping.';
COMMENT ON COLUMN integration.adapter_mappings.effective_from IS 'Start of mapping effectiveness.';
COMMENT ON COLUMN integration.adapter_mappings.effective_to IS 'End of mapping effectiveness.';
COMMENT ON COLUMN integration.adapter_mappings.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN integration.adapter_mappings.created_by_user_id IS 'User who created the mapping.';
COMMENT ON COLUMN integration.adapter_mappings.created_by_service_identity_id IS 'Service identity that created the mapping.';
COMMENT ON COLUMN integration.adapter_mappings.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN integration.adapter_mappings.updated_by_user_id IS 'User who last updated the mapping.';
COMMENT ON COLUMN integration.adapter_mappings.updated_by_service_identity_id IS 'Service identity that last updated the mapping.';
COMMENT ON COLUMN integration.adapter_mappings.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- integration.integration_credential_references
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS integration.integration_credential_references (

    integration_credential_reference_id uuid DEFAULT gen_random_uuid() NOT NULL,
    vendor_system_id uuid NOT NULL,
    service_identity_id uuid,
    credential_code varchar(64) NOT NULL,
    credential_name varchar(128) NOT NULL,
    credential_type integration.integration_credential_type_enum NOT NULL,
    secret_store_type integration.secret_store_type_enum NOT NULL,
    secret_reference varchar(256) NOT NULL,
    credential_status integration.integration_credential_status_enum NOT NULL,
    credential_version_ref varchar(128),
    last_rotated_at timestamptz,
    next_rotation_due_at timestamptz,
    expires_at timestamptz,
    revoked_at timestamptz,
    revocation_reason_code varchar(64),
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_integration_credential_references PRIMARY KEY (integration_credential_reference_id)
);
COMMENT ON TABLE integration.integration_credential_references IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN integration.integration_credential_references.integration_credential_reference_id IS 'Canonical identifier of the credential reference.';
COMMENT ON COLUMN integration.integration_credential_references.vendor_system_id IS 'Vendor system using the credential.';
COMMENT ON COLUMN integration.integration_credential_references.service_identity_id IS 'Service identity associated with the credential.';
COMMENT ON COLUMN integration.integration_credential_references.credential_code IS 'Stable credential reference code.';
COMMENT ON COLUMN integration.integration_credential_references.credential_name IS 'Human-readable credential reference name.';
COMMENT ON COLUMN integration.integration_credential_references.credential_type IS 'Credential type.';
COMMENT ON COLUMN integration.integration_credential_references.secret_store_type IS 'Secret store type.';
COMMENT ON COLUMN integration.integration_credential_references.secret_reference IS 'Vault/key-store reference to secret material.';
COMMENT ON COLUMN integration.integration_credential_references.credential_status IS 'Credential reference lifecycle status.';
COMMENT ON COLUMN integration.integration_credential_references.credential_version_ref IS 'Secret version or certificate version reference.';
COMMENT ON COLUMN integration.integration_credential_references.last_rotated_at IS 'Last rotation timestamp.';
COMMENT ON COLUMN integration.integration_credential_references.next_rotation_due_at IS 'Next required rotation timestamp.';
COMMENT ON COLUMN integration.integration_credential_references.expires_at IS 'Credential expiry timestamp.';
COMMENT ON COLUMN integration.integration_credential_references.revoked_at IS 'Credential revocation timestamp.';
COMMENT ON COLUMN integration.integration_credential_references.revocation_reason_code IS 'Controlled revocation reason.';
COMMENT ON COLUMN integration.integration_credential_references.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN integration.integration_credential_references.created_by_user_id IS 'User who created the credential reference.';
COMMENT ON COLUMN integration.integration_credential_references.created_by_service_identity_id IS 'Service identity that created the credential reference.';
COMMENT ON COLUMN integration.integration_credential_references.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN integration.integration_credential_references.updated_by_user_id IS 'User who last updated the credential reference.';
COMMENT ON COLUMN integration.integration_credential_references.updated_by_service_identity_id IS 'Service identity that last updated the credential reference.';
COMMENT ON COLUMN integration.integration_credential_references.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- integration.integration_health_records
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS integration.integration_health_records (

    integration_health_record_id uuid DEFAULT gen_random_uuid() NOT NULL,
    vendor_system_id uuid NOT NULL,
    vendor_endpoint_id uuid,
    site_group_id uuid,
    site_id uuid,
    incident_record_id uuid,
    health_status integration.integration_health_status_enum NOT NULL,
    health_check_type integration.integration_health_check_type_enum NOT NULL,
    http_status_code integer,
    latency_ms integer,
    failure_reason_code varchar(64),
    error_code varchar(64),
    error_detail_ref varchar(256),
    observed_at timestamptz NOT NULL,
    recovered_at timestamptz,
    observed_by_service_identity_id uuid NOT NULL,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_integration_health_records PRIMARY KEY (integration_health_record_id)
);
COMMENT ON TABLE integration.integration_health_records IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN integration.integration_health_records.integration_health_record_id IS 'Canonical identifier of the health record.';
COMMENT ON COLUMN integration.integration_health_records.vendor_system_id IS 'Vendor system being observed.';
COMMENT ON COLUMN integration.integration_health_records.vendor_endpoint_id IS 'Endpoint being observed, where endpoint-specific.';
COMMENT ON COLUMN integration.integration_health_records.site_group_id IS 'Site group affected, where applicable.';
COMMENT ON COLUMN integration.integration_health_records.site_id IS 'Site affected, where applicable.';
COMMENT ON COLUMN integration.integration_health_records.incident_record_id IS 'Related incident, where material.';
COMMENT ON COLUMN integration.integration_health_records.health_status IS 'Health status.';
COMMENT ON COLUMN integration.integration_health_records.health_check_type IS 'Health check type.';
COMMENT ON COLUMN integration.integration_health_records.http_status_code IS 'HTTP status code where applicable.';
COMMENT ON COLUMN integration.integration_health_records.latency_ms IS 'Observed latency in milliseconds.';
COMMENT ON COLUMN integration.integration_health_records.failure_reason_code IS 'Controlled failure or degradation reason.';
COMMENT ON COLUMN integration.integration_health_records.error_code IS 'Vendor or adapter error code.';
COMMENT ON COLUMN integration.integration_health_records.error_detail_ref IS 'Reference to detailed diagnostic payload if retained.';
COMMENT ON COLUMN integration.integration_health_records.observed_at IS 'Timestamp when health was observed.';
COMMENT ON COLUMN integration.integration_health_records.recovered_at IS 'Recovery timestamp, where recorded in this health record.';
COMMENT ON COLUMN integration.integration_health_records.observed_by_service_identity_id IS 'Service identity that observed or recorded health.';
COMMENT ON COLUMN integration.integration_health_records.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN integration.integration_health_records.created_at IS 'Record creation timestamp.';

-- ------------------------------------------------------------
-- config.system_parameters
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS config.system_parameters (

    system_parameter_id uuid DEFAULT gen_random_uuid() NOT NULL,
    parameter_code varchar(96) NOT NULL,
    parameter_name varchar(128) NOT NULL,
    parameter_description text,
    parameter_domain varchar(64) NOT NULL,
    parameter_type config.system_parameter_type_enum NOT NULL,
    value_text text,
    value_numeric numeric(18,4),
    value_boolean boolean,
    value_json_ref varchar(256),
    parameter_status config.system_parameter_status_enum NOT NULL,
    requires_approval boolean DEFAULT false NOT NULL,
    is_sensitive boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    approved_at timestamptz,
    approved_by_user_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_system_parameters PRIMARY KEY (system_parameter_id)
);
COMMENT ON TABLE config.system_parameters IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN config.system_parameters.system_parameter_id IS 'Canonical identifier of the system parameter.';
COMMENT ON COLUMN config.system_parameters.parameter_code IS 'Stable parameter code.';
COMMENT ON COLUMN config.system_parameters.parameter_name IS 'Human-readable parameter name.';
COMMENT ON COLUMN config.system_parameters.parameter_description IS 'Description of the parameter and intended use.';
COMMENT ON COLUMN config.system_parameters.parameter_domain IS 'Domain or service area where parameter applies.';
COMMENT ON COLUMN config.system_parameters.parameter_type IS 'Data type of the parameter value.';
COMMENT ON COLUMN config.system_parameters.value_text IS 'Text parameter value.';
COMMENT ON COLUMN config.system_parameters.value_numeric IS 'Numeric parameter value.';
COMMENT ON COLUMN config.system_parameters.value_boolean IS 'Boolean parameter value.';
COMMENT ON COLUMN config.system_parameters.value_json_ref IS 'Reference to structured configuration if stored externally.';
COMMENT ON COLUMN config.system_parameters.parameter_status IS 'Parameter lifecycle status.';
COMMENT ON COLUMN config.system_parameters.requires_approval IS 'Indicates whether changes require approval.';
COMMENT ON COLUMN config.system_parameters.is_sensitive IS 'Indicates sensitive configuration metadata. Must not mean secret storage.';
COMMENT ON COLUMN config.system_parameters.effective_from IS 'Start of parameter effectiveness.';
COMMENT ON COLUMN config.system_parameters.effective_to IS 'End of parameter effectiveness.';
COMMENT ON COLUMN config.system_parameters.approved_at IS 'Approval timestamp, where required.';
COMMENT ON COLUMN config.system_parameters.approved_by_user_id IS 'User who approved the parameter.';
COMMENT ON COLUMN config.system_parameters.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN config.system_parameters.created_by_user_id IS 'User who created the parameter.';
COMMENT ON COLUMN config.system_parameters.created_by_service_identity_id IS 'Service identity that created the parameter.';
COMMENT ON COLUMN config.system_parameters.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN config.system_parameters.updated_by_user_id IS 'User who last updated the parameter.';
COMMENT ON COLUMN config.system_parameters.updated_by_service_identity_id IS 'Service identity that last updated the parameter.';
COMMENT ON COLUMN config.system_parameters.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- config.feature_flags
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS config.feature_flags (

    feature_flag_id uuid DEFAULT gen_random_uuid() NOT NULL,
    flag_code varchar(96) NOT NULL,
    flag_name varchar(128) NOT NULL,
    flag_description text,
    flag_domain varchar(64) NOT NULL,
    flag_status config.feature_flag_status_enum NOT NULL,
    enabled boolean DEFAULT false NOT NULL,
    environment_code varchar(32),
    site_group_id uuid,
    site_id uuid,
    merchant_id uuid,
    payment_rail_id uuid,
    service_identity_id uuid,
    requires_approval boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    approved_at timestamptz,
    approved_by_user_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_feature_flags PRIMARY KEY (feature_flag_id)
);
COMMENT ON TABLE config.feature_flags IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN config.feature_flags.feature_flag_id IS 'Canonical identifier of the feature flag.';
COMMENT ON COLUMN config.feature_flags.flag_code IS 'Stable feature flag code.';
COMMENT ON COLUMN config.feature_flags.flag_name IS 'Human-readable flag name.';
COMMENT ON COLUMN config.feature_flags.flag_description IS 'Description of flag purpose and effect.';
COMMENT ON COLUMN config.feature_flags.flag_domain IS 'Domain or service area where flag applies.';
COMMENT ON COLUMN config.feature_flags.flag_status IS 'Flag lifecycle status.';
COMMENT ON COLUMN config.feature_flags.enabled IS 'Current enabled state.';
COMMENT ON COLUMN config.feature_flags.environment_code IS 'Environment where the flag applies.';
COMMENT ON COLUMN config.feature_flags.site_group_id IS 'Site group scope, where applicable.';
COMMENT ON COLUMN config.feature_flags.site_id IS 'Site scope, where applicable.';
COMMENT ON COLUMN config.feature_flags.merchant_id IS 'Merchant scope, where applicable.';
COMMENT ON COLUMN config.feature_flags.payment_rail_id IS 'Payment rail scope, where applicable.';
COMMENT ON COLUMN config.feature_flags.service_identity_id IS 'Service identity scope, where applicable.';
COMMENT ON COLUMN config.feature_flags.requires_approval IS 'Indicates whether changes require approval.';
COMMENT ON COLUMN config.feature_flags.effective_from IS 'Start of flag effectiveness.';
COMMENT ON COLUMN config.feature_flags.effective_to IS 'End of flag effectiveness.';
COMMENT ON COLUMN config.feature_flags.approved_at IS 'Approval timestamp, where required.';
COMMENT ON COLUMN config.feature_flags.approved_by_user_id IS 'User who approved the flag.';
COMMENT ON COLUMN config.feature_flags.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN config.feature_flags.created_by_user_id IS 'User who created the flag.';
COMMENT ON COLUMN config.feature_flags.created_by_service_identity_id IS 'Service identity that created the flag.';
COMMENT ON COLUMN config.feature_flags.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN config.feature_flags.updated_by_user_id IS 'User who last updated the flag.';
COMMENT ON COLUMN config.feature_flags.updated_by_service_identity_id IS 'Service identity that last updated the flag.';
COMMENT ON COLUMN config.feature_flags.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- config.rate_limit_policies
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS config.rate_limit_policies (

    rate_limit_policy_id uuid DEFAULT gen_random_uuid() NOT NULL,
    policy_code varchar(96) NOT NULL,
    policy_name varchar(128) NOT NULL,
    policy_description text,
    policy_domain varchar(64) NOT NULL,
    scope_type config.rate_limit_scope_type_enum NOT NULL,
    window_seconds integer NOT NULL,
    max_requests integer NOT NULL,
    burst_limit integer,
    penalty_seconds integer,
    policy_status config.rate_limit_policy_status_enum NOT NULL,
    enforcement_mode config.rate_limit_enforcement_mode_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_rate_limit_policies PRIMARY KEY (rate_limit_policy_id)
);
COMMENT ON TABLE config.rate_limit_policies IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN config.rate_limit_policies.rate_limit_policy_id IS 'Canonical identifier of the rate-limit policy.';
COMMENT ON COLUMN config.rate_limit_policies.policy_code IS 'Stable policy code.';
COMMENT ON COLUMN config.rate_limit_policies.policy_name IS 'Human-readable policy name.';
COMMENT ON COLUMN config.rate_limit_policies.policy_description IS 'Description of policy purpose.';
COMMENT ON COLUMN config.rate_limit_policies.policy_domain IS 'Domain or service area where policy applies.';
COMMENT ON COLUMN config.rate_limit_policies.scope_type IS 'Scope of the rate limit.';
COMMENT ON COLUMN config.rate_limit_policies.window_seconds IS 'Rolling or fixed window duration in seconds.';
COMMENT ON COLUMN config.rate_limit_policies.max_requests IS 'Maximum allowed requests in the window.';
COMMENT ON COLUMN config.rate_limit_policies.burst_limit IS 'Optional burst allowance.';
COMMENT ON COLUMN config.rate_limit_policies.penalty_seconds IS 'Lockout or penalty duration after violation.';
COMMENT ON COLUMN config.rate_limit_policies.policy_status IS 'Policy lifecycle status.';
COMMENT ON COLUMN config.rate_limit_policies.enforcement_mode IS 'Enforcement behavior.';
COMMENT ON COLUMN config.rate_limit_policies.effective_from IS 'Start of policy effectiveness.';
COMMENT ON COLUMN config.rate_limit_policies.effective_to IS 'End of policy effectiveness.';
COMMENT ON COLUMN config.rate_limit_policies.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN config.rate_limit_policies.created_by_user_id IS 'User who created the policy.';
COMMENT ON COLUMN config.rate_limit_policies.created_by_service_identity_id IS 'Service identity that created the policy.';
COMMENT ON COLUMN config.rate_limit_policies.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN config.rate_limit_policies.updated_by_user_id IS 'User who last updated the policy.';
COMMENT ON COLUMN config.rate_limit_policies.updated_by_service_identity_id IS 'Service identity that last updated the policy.';
COMMENT ON COLUMN config.rate_limit_policies.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- config.ttl_policies
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS config.ttl_policies (

    ttl_policy_id uuid DEFAULT gen_random_uuid() NOT NULL,
    policy_code varchar(96) NOT NULL,
    policy_name varchar(128) NOT NULL,
    policy_description text,
    policy_domain varchar(64) NOT NULL,
    ttl_scope_type config.ttl_scope_type_enum NOT NULL,
    ttl_seconds integer NOT NULL,
    grace_period_seconds integer,
    expiry_action config.ttl_expiry_action_enum NOT NULL,
    policy_status config.ttl_policy_status_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_ttl_policies PRIMARY KEY (ttl_policy_id)
);
COMMENT ON TABLE config.ttl_policies IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN config.ttl_policies.ttl_policy_id IS 'Canonical identifier of the TTL policy.';
COMMENT ON COLUMN config.ttl_policies.policy_code IS 'Stable TTL policy code.';
COMMENT ON COLUMN config.ttl_policies.policy_name IS 'Human-readable policy name.';
COMMENT ON COLUMN config.ttl_policies.policy_description IS 'Description of policy purpose.';
COMMENT ON COLUMN config.ttl_policies.policy_domain IS 'Domain or workflow where policy applies.';
COMMENT ON COLUMN config.ttl_policies.ttl_scope_type IS 'Expiry scope.';
COMMENT ON COLUMN config.ttl_policies.ttl_seconds IS 'TTL duration in seconds.';
COMMENT ON COLUMN config.ttl_policies.grace_period_seconds IS 'Optional grace period for support or cleanup, not validity extension unless domain allows.';
COMMENT ON COLUMN config.ttl_policies.expiry_action IS 'Action expected when TTL expires.';
COMMENT ON COLUMN config.ttl_policies.policy_status IS 'TTL policy lifecycle status.';
COMMENT ON COLUMN config.ttl_policies.effective_from IS 'Start of policy effectiveness.';
COMMENT ON COLUMN config.ttl_policies.effective_to IS 'End of policy effectiveness.';
COMMENT ON COLUMN config.ttl_policies.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN config.ttl_policies.created_by_user_id IS 'User who created the policy.';
COMMENT ON COLUMN config.ttl_policies.created_by_service_identity_id IS 'Service identity that created the policy.';
COMMENT ON COLUMN config.ttl_policies.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN config.ttl_policies.updated_by_user_id IS 'User who last updated the policy.';
COMMENT ON COLUMN config.ttl_policies.updated_by_service_identity_id IS 'Service identity that last updated the policy.';
COMMENT ON COLUMN config.ttl_policies.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- config.controlled_code_sets
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS config.controlled_code_sets (

    controlled_code_set_id uuid DEFAULT gen_random_uuid() NOT NULL,
    code_set_name varchar(96) NOT NULL,
    code_value varchar(96) NOT NULL,
    code_label varchar(128) NOT NULL,
    code_description text,
    code_domain varchar(64) NOT NULL,
    code_status config.controlled_code_status_enum NOT NULL,
    sort_order integer,
    requires_comment boolean DEFAULT false NOT NULL,
    requires_approval boolean DEFAULT false NOT NULL,
    is_sensitive boolean DEFAULT false NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_controlled_code_sets PRIMARY KEY (controlled_code_set_id)
);
COMMENT ON TABLE config.controlled_code_sets IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN config.controlled_code_sets.controlled_code_set_id IS 'Canonical identifier of the controlled code.';
COMMENT ON COLUMN config.controlled_code_sets.code_set_name IS 'Name of the code set.';
COMMENT ON COLUMN config.controlled_code_sets.code_value IS 'Controlled code value.';
COMMENT ON COLUMN config.controlled_code_sets.code_label IS 'Human-readable label.';
COMMENT ON COLUMN config.controlled_code_sets.code_description IS 'Description of code meaning and use.';
COMMENT ON COLUMN config.controlled_code_sets.code_domain IS 'Domain where code primarily applies.';
COMMENT ON COLUMN config.controlled_code_sets.code_status IS 'Code lifecycle status.';
COMMENT ON COLUMN config.controlled_code_sets.sort_order IS 'Optional display or evaluation order.';
COMMENT ON COLUMN config.controlled_code_sets.requires_comment IS 'Indicates whether use of the code requires a note.';
COMMENT ON COLUMN config.controlled_code_sets.requires_approval IS 'Indicates whether use of the code requires approval.';
COMMENT ON COLUMN config.controlled_code_sets.is_sensitive IS 'Indicates whether the code is sensitive or restricted in use.';
COMMENT ON COLUMN config.controlled_code_sets.effective_from IS 'Start of code effectiveness.';
COMMENT ON COLUMN config.controlled_code_sets.effective_to IS 'End of code effectiveness.';
COMMENT ON COLUMN config.controlled_code_sets.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN config.controlled_code_sets.created_by_user_id IS 'User who created the code.';
COMMENT ON COLUMN config.controlled_code_sets.created_by_service_identity_id IS 'Service identity that created the code.';
COMMENT ON COLUMN config.controlled_code_sets.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN config.controlled_code_sets.updated_by_user_id IS 'User who last updated the code.';
COMMENT ON COLUMN config.controlled_code_sets.updated_by_service_identity_id IS 'Service identity that last updated the code.';
COMMENT ON COLUMN config.controlled_code_sets.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- events.domain_events
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS events.domain_events (

    domain_event_id uuid DEFAULT gen_random_uuid() NOT NULL,
    source_schema varchar(64) NOT NULL,
    source_table varchar(96),
    event_type varchar(128) NOT NULL,
    event_version integer DEFAULT 1 NOT NULL,
    aggregate_type varchar(96) NOT NULL,
    aggregate_id uuid NOT NULL,
    related_entity_type varchar(96),
    related_entity_id uuid,
    event_status events.domain_event_status_enum NOT NULL,
    payload_ref varchar(256),
    payload_hash char(64),
    metadata_ref varchar(256),
    occurred_at timestamptz NOT NULL,
    recorded_at timestamptz DEFAULT now() NOT NULL,
    actor_user_id uuid,
    actor_service_identity_id uuid,
    correlation_id uuid,
    causation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_domain_events PRIMARY KEY (domain_event_id)
);
COMMENT ON TABLE events.domain_events IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN events.domain_events.domain_event_id IS 'Canonical identifier of the domain event.';
COMMENT ON COLUMN events.domain_events.source_schema IS 'Source schema that raised the event.';
COMMENT ON COLUMN events.domain_events.source_table IS 'Source table associated with the event.';
COMMENT ON COLUMN events.domain_events.event_type IS 'Stable domain event type.';
COMMENT ON COLUMN events.domain_events.event_version IS 'Event schema version.';
COMMENT ON COLUMN events.domain_events.aggregate_type IS 'Aggregate or domain object type.';
COMMENT ON COLUMN events.domain_events.aggregate_id IS 'Aggregate or domain record identifier.';
COMMENT ON COLUMN events.domain_events.related_entity_type IS 'Related entity type, where applicable.';
COMMENT ON COLUMN events.domain_events.related_entity_id IS 'Related entity identifier, where applicable.';
COMMENT ON COLUMN events.domain_events.event_status IS 'Domain event lifecycle status.';
COMMENT ON COLUMN events.domain_events.payload_ref IS 'Reference to event payload if stored externally.';
COMMENT ON COLUMN events.domain_events.payload_hash IS 'Hash of event payload.';
COMMENT ON COLUMN events.domain_events.metadata_ref IS 'Reference to event metadata if stored externally.';
COMMENT ON COLUMN events.domain_events.occurred_at IS 'Timestamp when source-domain fact occurred.';
COMMENT ON COLUMN events.domain_events.recorded_at IS 'Timestamp when event was recorded.';
COMMENT ON COLUMN events.domain_events.actor_user_id IS 'Human actor associated with the event, where applicable.';
COMMENT ON COLUMN events.domain_events.actor_service_identity_id IS 'Service, worker, adapter, or device actor associated with the event.';
COMMENT ON COLUMN events.domain_events.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN events.domain_events.causation_id IS 'Causation identifier where this event was caused by another action or event.';
COMMENT ON COLUMN events.domain_events.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN events.domain_events.created_by_service_identity_id IS 'Service identity that wrote the event record.';

-- ------------------------------------------------------------
-- events.outbox_events
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS events.outbox_events (

    outbox_event_id uuid DEFAULT gen_random_uuid() NOT NULL,
    domain_event_id uuid,
    source_schema varchar(64) NOT NULL,
    source_table varchar(96),
    event_type varchar(128) NOT NULL,
    event_version integer DEFAULT 1 NOT NULL,
    aggregate_type varchar(96) NOT NULL,
    aggregate_id uuid NOT NULL,
    routing_key varchar(160) NOT NULL,
    exchange_name varchar(128),
    payload_ref varchar(256),
    payload_hash char(64),
    payload_content_type varchar(64) NOT NULL,
    publication_status events.outbox_publication_status_enum NOT NULL,
    available_at timestamptz DEFAULT now() NOT NULL,
    locked_at timestamptz,
    locked_by_service_identity_id uuid,
    published_at timestamptz,
    next_retry_at timestamptz,
    retry_count integer DEFAULT 0 NOT NULL,
    max_retry_count integer DEFAULT 10 NOT NULL,
    failure_reason_code varchar(64),
    correlation_id uuid,
    causation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_outbox_events PRIMARY KEY (outbox_event_id)
);
COMMENT ON TABLE events.outbox_events IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN events.outbox_events.outbox_event_id IS 'Canonical identifier of the outbox event.';
COMMENT ON COLUMN events.outbox_events.domain_event_id IS 'Related domain event, where domain_events is used.';
COMMENT ON COLUMN events.outbox_events.source_schema IS 'Source schema that produced the outbox event.';
COMMENT ON COLUMN events.outbox_events.source_table IS 'Source table associated with the event.';
COMMENT ON COLUMN events.outbox_events.event_type IS 'Event type to publish.';
COMMENT ON COLUMN events.outbox_events.event_version IS 'Published event schema version.';
COMMENT ON COLUMN events.outbox_events.aggregate_type IS 'Aggregate or source domain object type.';
COMMENT ON COLUMN events.outbox_events.aggregate_id IS 'Aggregate or source domain object identifier.';
COMMENT ON COLUMN events.outbox_events.routing_key IS 'Broker routing key or topic name.';
COMMENT ON COLUMN events.outbox_events.exchange_name IS 'Broker exchange name, where applicable.';
COMMENT ON COLUMN events.outbox_events.payload_ref IS 'Reference to event payload if stored externally.';
COMMENT ON COLUMN events.outbox_events.payload_hash IS 'Hash of event payload.';
COMMENT ON COLUMN events.outbox_events.payload_content_type IS 'Payload content type.';
COMMENT ON COLUMN events.outbox_events.publication_status IS 'Outbox publication lifecycle status.';
COMMENT ON COLUMN events.outbox_events.available_at IS 'Timestamp when event becomes eligible for dispatch.';
COMMENT ON COLUMN events.outbox_events.locked_at IS 'Timestamp when dispatcher locked the event for processing.';
COMMENT ON COLUMN events.outbox_events.locked_by_service_identity_id IS 'Dispatcher service identity that locked the event.';
COMMENT ON COLUMN events.outbox_events.published_at IS 'Timestamp when publication succeeded.';
COMMENT ON COLUMN events.outbox_events.next_retry_at IS 'Next retry timestamp after failed publication.';
COMMENT ON COLUMN events.outbox_events.retry_count IS 'Number of publication attempts.';
COMMENT ON COLUMN events.outbox_events.max_retry_count IS 'Maximum allowed retry attempts before dead-letter handling.';
COMMENT ON COLUMN events.outbox_events.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN events.outbox_events.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN events.outbox_events.causation_id IS 'Causation identifier.';
COMMENT ON COLUMN events.outbox_events.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN events.outbox_events.created_by_service_identity_id IS 'Service identity that created the outbox event.';
COMMENT ON COLUMN events.outbox_events.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN events.outbox_events.updated_by_service_identity_id IS 'Service identity that last updated the outbox event.';
COMMENT ON COLUMN events.outbox_events.row_version IS 'Optimistic concurrency version for dispatcher safety.';

-- ------------------------------------------------------------
-- events.event_publications
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS events.event_publications (

    event_publication_id uuid DEFAULT gen_random_uuid() NOT NULL,
    outbox_event_id uuid NOT NULL,
    publication_attempt_number integer NOT NULL,
    publisher_service_identity_id uuid NOT NULL,
    broker_type events.event_broker_type_enum NOT NULL,
    exchange_name varchar(128),
    routing_key varchar(160),
    publication_status events.event_publication_status_enum NOT NULL,
    broker_message_id varchar(128),
    broker_acknowledged boolean,
    failure_reason_code varchar(64),
    failure_detail_ref varchar(256),
    started_at timestamptz DEFAULT now() NOT NULL,
    completed_at timestamptz,
    duration_ms integer,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_event_publications PRIMARY KEY (event_publication_id)
);
COMMENT ON TABLE events.event_publications IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN events.event_publications.event_publication_id IS 'Canonical identifier of the publication attempt.';
COMMENT ON COLUMN events.event_publications.outbox_event_id IS 'Outbox event being published.';
COMMENT ON COLUMN events.event_publications.publication_attempt_number IS 'Sequential attempt number for the outbox event.';
COMMENT ON COLUMN events.event_publications.publisher_service_identity_id IS 'Dispatcher or publisher service identity.';
COMMENT ON COLUMN events.event_publications.broker_type IS 'Broker or transport type.';
COMMENT ON COLUMN events.event_publications.exchange_name IS 'Exchange name used.';
COMMENT ON COLUMN events.event_publications.routing_key IS 'Routing key or topic used.';
COMMENT ON COLUMN events.event_publications.publication_status IS 'Publication attempt result.';
COMMENT ON COLUMN events.event_publications.broker_message_id IS 'Broker-assigned message ID, where available.';
COMMENT ON COLUMN events.event_publications.broker_acknowledged IS 'Whether broker acknowledged the publication.';
COMMENT ON COLUMN events.event_publications.failure_reason_code IS 'Controlled publication failure reason.';
COMMENT ON COLUMN events.event_publications.failure_detail_ref IS 'Reference to detailed failure evidence, if retained.';
COMMENT ON COLUMN events.event_publications.started_at IS 'Publication attempt start timestamp.';
COMMENT ON COLUMN events.event_publications.completed_at IS 'Publication attempt completion timestamp.';
COMMENT ON COLUMN events.event_publications.duration_ms IS 'Publication duration in milliseconds.';
COMMENT ON COLUMN events.event_publications.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN events.event_publications.created_at IS 'Record creation timestamp.';

-- ------------------------------------------------------------
-- events.dead_letter_records
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS events.dead_letter_records (

    dead_letter_record_id uuid DEFAULT gen_random_uuid() NOT NULL,
    outbox_event_id uuid,
    event_publication_id uuid,
    consumer_name varchar(128),
    dead_letter_type events.dead_letter_type_enum NOT NULL,
    dead_letter_status events.dead_letter_status_enum NOT NULL,
    failure_reason_code varchar(64) NOT NULL,
    failure_detail_ref varchar(256),
    payload_hash char(64),
    dead_lettered_at timestamptz DEFAULT now() NOT NULL,
    resolved_at timestamptz,
    resolved_by_user_id uuid,
    resolved_by_service_identity_id uuid,
    resolution_reason_code varchar(64),
    replay_requested_at timestamptz,
    replay_requested_by_user_id uuid,
    replay_requested_by_service_identity_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_dead_letter_records PRIMARY KEY (dead_letter_record_id)
);
COMMENT ON TABLE events.dead_letter_records IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN events.dead_letter_records.dead_letter_record_id IS 'Canonical identifier of the dead-letter record.';
COMMENT ON COLUMN events.dead_letter_records.outbox_event_id IS 'Outbox event that dead-lettered.';
COMMENT ON COLUMN events.dead_letter_records.event_publication_id IS 'Publication attempt that caused dead-lettering, where applicable.';
COMMENT ON COLUMN events.dead_letter_records.consumer_name IS 'Consumer that dead-lettered the event, where consumer-side dead-lettering is recorded.';
COMMENT ON COLUMN events.dead_letter_records.dead_letter_type IS 'Dead-letter category.';
COMMENT ON COLUMN events.dead_letter_records.dead_letter_status IS 'Dead-letter lifecycle status.';
COMMENT ON COLUMN events.dead_letter_records.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN events.dead_letter_records.failure_detail_ref IS 'Reference to detailed failure evidence.';
COMMENT ON COLUMN events.dead_letter_records.payload_hash IS 'Payload hash associated with dead-lettered event.';
COMMENT ON COLUMN events.dead_letter_records.dead_lettered_at IS 'Timestamp when event was dead-lettered.';
COMMENT ON COLUMN events.dead_letter_records.resolved_at IS 'Timestamp when dead-letter was resolved.';
COMMENT ON COLUMN events.dead_letter_records.resolved_by_user_id IS 'User who resolved dead-letter record.';
COMMENT ON COLUMN events.dead_letter_records.resolved_by_service_identity_id IS 'Service identity that resolved dead-letter record.';
COMMENT ON COLUMN events.dead_letter_records.resolution_reason_code IS 'Controlled resolution reason.';
COMMENT ON COLUMN events.dead_letter_records.replay_requested_at IS 'Timestamp when replay was requested.';
COMMENT ON COLUMN events.dead_letter_records.replay_requested_by_user_id IS 'User who requested replay.';
COMMENT ON COLUMN events.dead_letter_records.replay_requested_by_service_identity_id IS 'Service identity that requested replay.';
COMMENT ON COLUMN events.dead_letter_records.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN events.dead_letter_records.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN events.dead_letter_records.created_by_service_identity_id IS 'Service identity that created the dead-letter record.';
COMMENT ON COLUMN events.dead_letter_records.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN events.dead_letter_records.updated_by_user_id IS 'User who last updated the dead-letter record.';
COMMENT ON COLUMN events.dead_letter_records.updated_by_service_identity_id IS 'Service identity that last updated the dead-letter record.';
COMMENT ON COLUMN events.dead_letter_records.row_version IS 'Optimistic concurrency version.';

-- ------------------------------------------------------------
-- events.consumer_checkpoints
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS events.consumer_checkpoints (

    consumer_checkpoint_id uuid DEFAULT gen_random_uuid() NOT NULL,
    consumer_name varchar(128) NOT NULL,
    consumer_group varchar(128),
    subscription_name varchar(128),
    event_type varchar(128),
    aggregate_type varchar(96),
    last_outbox_event_id uuid,
    last_domain_event_id uuid,
    last_broker_offset varchar(128),
    checkpoint_status events.consumer_checkpoint_status_enum NOT NULL,
    processed_count bigint DEFAULT 0 NOT NULL,
    failure_count bigint DEFAULT 0 NOT NULL,
    last_processed_at timestamptz,
    last_failed_at timestamptz,
    failure_reason_code varchar(64),
    locked_at timestamptz,
    locked_by_service_identity_id uuid,
    updated_by_service_identity_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    correlation_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_consumer_checkpoints PRIMARY KEY (consumer_checkpoint_id)
);
COMMENT ON TABLE events.consumer_checkpoints IS 'ExitPass v1.2 table generated from Section 13 physical table specifications.';
COMMENT ON COLUMN events.consumer_checkpoints.consumer_checkpoint_id IS 'Canonical identifier of the consumer checkpoint.';
COMMENT ON COLUMN events.consumer_checkpoints.consumer_name IS 'Stable consumer name.';
COMMENT ON COLUMN events.consumer_checkpoints.consumer_group IS 'Consumer group name, where applicable.';
COMMENT ON COLUMN events.consumer_checkpoints.subscription_name IS 'Queue, subscription, topic, or routing subscription name.';
COMMENT ON COLUMN events.consumer_checkpoints.event_type IS 'Event type checkpoint applies to, if scoped.';
COMMENT ON COLUMN events.consumer_checkpoints.aggregate_type IS 'Aggregate type checkpoint applies to, if scoped.';
COMMENT ON COLUMN events.consumer_checkpoints.last_outbox_event_id IS 'Last processed outbox event.';
COMMENT ON COLUMN events.consumer_checkpoints.last_domain_event_id IS 'Last processed domain event, where applicable.';
COMMENT ON COLUMN events.consumer_checkpoints.last_broker_offset IS 'Broker offset, delivery tag, sequence, or cursor.';
COMMENT ON COLUMN events.consumer_checkpoints.checkpoint_status IS 'Checkpoint lifecycle or processing status.';
COMMENT ON COLUMN events.consumer_checkpoints.processed_count IS 'Count of processed events tracked by this checkpoint.';
COMMENT ON COLUMN events.consumer_checkpoints.failure_count IS 'Count of processing failures tracked by this checkpoint.';
COMMENT ON COLUMN events.consumer_checkpoints.last_processed_at IS 'Timestamp when last event was processed.';
COMMENT ON COLUMN events.consumer_checkpoints.last_failed_at IS 'Timestamp of last processing failure.';
COMMENT ON COLUMN events.consumer_checkpoints.failure_reason_code IS 'Controlled failure reason.';
COMMENT ON COLUMN events.consumer_checkpoints.locked_at IS 'Timestamp when checkpoint was locked for processing.';
COMMENT ON COLUMN events.consumer_checkpoints.locked_by_service_identity_id IS 'Consumer service identity holding the lock.';
COMMENT ON COLUMN events.consumer_checkpoints.updated_by_service_identity_id IS 'Consumer service identity that last updated the checkpoint.';
COMMENT ON COLUMN events.consumer_checkpoints.created_at IS 'Record creation timestamp.';
COMMENT ON COLUMN events.consumer_checkpoints.updated_at IS 'Last update timestamp.';
COMMENT ON COLUMN events.consumer_checkpoints.correlation_id IS 'Cross-service correlation identifier.';
COMMENT ON COLUMN events.consumer_checkpoints.row_version IS 'Optimistic concurrency version for checkpoint safety.';

-- ============================================================
-- FOREIGN KEYS
-- ============================================================
ALTER TABLE core.parking_sessions ADD CONSTRAINT fk_parking_sessions__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.parking_sessions ADD CONSTRAINT fk_parking_sessions__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.parking_sessions ADD CONSTRAINT fk_parking_sessions__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.parking_sessions ADD CONSTRAINT fk_parking_sessions__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.parking_sessions ADD CONSTRAINT fk_parking_sessions__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__superseded_by_tariff_snapshot_id FOREIGN KEY (superseded_by_tariff_snapshot_id) REFERENCES core.tariff_snapshots(tariff_snapshot_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__statutory_discount_validation_id FOREIGN KEY (statutory_discount_validation_id) REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__coupon_application_id FOREIGN KEY (coupon_application_id) REFERENCES coupons.coupon_applications(coupon_application_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT fk_tariff_snapshots__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_attempts ADD CONSTRAINT fk_payment_attempts__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_attempts ADD CONSTRAINT fk_payment_attempts__tariff_snapshot_id FOREIGN KEY (tariff_snapshot_id) REFERENCES core.tariff_snapshots(tariff_snapshot_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_attempts ADD CONSTRAINT fk_payment_attempts__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_attempts ADD CONSTRAINT fk_payment_attempts__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_attempts ADD CONSTRAINT fk_payment_attempts__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_confirmations ADD CONSTRAINT fk_payment_confirmations__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_confirmations ADD CONSTRAINT fk_payment_confirmations__provider_outcome_id FOREIGN KEY (provider_outcome_id) REFERENCES payments.provider_outcomes(provider_outcome_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_confirmations ADD CONSTRAINT fk_payment_confirmations__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.payment_confirmations ADD CONSTRAINT fk_payment_confirmations__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.exit_authorizations ADD CONSTRAINT fk_exit_authorizations__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.exit_authorizations ADD CONSTRAINT fk_exit_authorizations__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.exit_authorizations ADD CONSTRAINT fk_exit_authorizations__payment_confirmation_id FOREIGN KEY (payment_confirmation_id) REFERENCES core.payment_confirmations(payment_confirmation_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.exit_authorizations ADD CONSTRAINT fk_exit_authorizations__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE core.exit_authorizations ADD CONSTRAINT fk_exit_authorizations__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.payment_rails ADD CONSTRAINT fk_payment_rails__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.payment_rails ADD CONSTRAINT fk_payment_rails__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.payment_rails ADD CONSTRAINT fk_payment_rails__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.payment_rails ADD CONSTRAINT fk_payment_rails__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_sessions ADD CONSTRAINT fk_provider_sessions__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_sessions ADD CONSTRAINT fk_provider_sessions__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_sessions ADD CONSTRAINT fk_provider_sessions__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_sessions ADD CONSTRAINT fk_provider_sessions__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_callbacks ADD CONSTRAINT fk_provider_callbacks__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_callbacks ADD CONSTRAINT fk_provider_callbacks__provider_session_id FOREIGN KEY (provider_session_id) REFERENCES payments.provider_sessions(provider_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_callbacks ADD CONSTRAINT fk_provider_callbacks__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_callbacks ADD CONSTRAINT fk_provider_callbacks__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_status_queries ADD CONSTRAINT fk_provider_status_queries__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_status_queries ADD CONSTRAINT fk_provider_status_queries__provider_session_id FOREIGN KEY (provider_session_id) REFERENCES payments.provider_sessions(provider_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_status_queries ADD CONSTRAINT fk_provider_status_queries__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_status_queries ADD CONSTRAINT fk_provider_status_queries__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT fk_provider_outcomes__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT fk_provider_outcomes__provider_session_id FOREIGN KEY (provider_session_id) REFERENCES payments.provider_sessions(provider_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT fk_provider_outcomes__provider_callback_id FOREIGN KEY (provider_callback_id) REFERENCES payments.provider_callbacks(provider_callback_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT fk_provider_outcomes__provider_status_query_id FOREIGN KEY (provider_status_query_id) REFERENCES payments.provider_status_queries(provider_status_query_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT fk_provider_outcomes__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT fk_provider_outcomes__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT fk_provider_outcomes__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_requests ADD CONSTRAINT fk_session_resolution_requests__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_requests ADD CONSTRAINT fk_session_resolution_requests__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_requests ADD CONSTRAINT fk_session_resolution_requests__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_requests ADD CONSTRAINT fk_session_resolution_requests__created_by_service_identity_ FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT fk_session_resolution_results__session_resolution_request_id FOREIGN KEY (session_resolution_request_id) REFERENCES sessions.session_resolution_requests(session_resolution_request_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT fk_session_resolution_results__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT fk_session_resolution_results__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT fk_session_resolution_results__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT fk_session_resolution_results__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT fk_session_resolution_results__created_by_service_identity_i FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_lookup_cache ADD CONSTRAINT fk_session_lookup_cache__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_lookup_cache ADD CONSTRAINT fk_session_lookup_cache__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_lookup_cache ADD CONSTRAINT fk_session_lookup_cache__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_lookup_cache ADD CONSTRAINT fk_session_lookup_cache__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_lookup_cache ADD CONSTRAINT fk_session_lookup_cache__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT fk_session_identifier_indexes__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT fk_session_identifier_indexes__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT fk_session_identifier_indexes__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT fk_session_identifier_indexes__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT fk_session_identifier_indexes__created_by_service_identity_i FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT fk_session_identifier_indexes__updated_by_service_identity_i FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupons ADD CONSTRAINT fk_coupons__merchant_id FOREIGN KEY (merchant_id) REFERENCES merchants.merchants(merchant_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupons ADD CONSTRAINT fk_coupons__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupons ADD CONSTRAINT fk_coupons__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupons ADD CONSTRAINT fk_coupons__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupons ADD CONSTRAINT fk_coupons__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT fk_coupon_rule_groups__coupon_id FOREIGN KEY (coupon_id) REFERENCES coupons.coupons(coupon_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT fk_coupon_rule_groups__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT fk_coupon_rule_groups__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT fk_coupon_rule_groups__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT fk_coupon_rule_groups__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__coupon_rule_group_id FOREIGN KEY (coupon_rule_group_id) REFERENCES coupons.coupon_rule_groups(coupon_rule_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__merchant_id FOREIGN KEY (merchant_id) REFERENCES merchants.merchants(merchant_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT fk_coupon_rules__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__coupon_id FOREIGN KEY (coupon_id) REFERENCES coupons.coupons(coupon_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__merchant_id FOREIGN KEY (merchant_id) REFERENCES merchants.merchants(merchant_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__merchant_wallet_id FOREIGN KEY (merchant_wallet_id) REFERENCES merchants.merchant_wallets(merchant_wallet_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__tariff_snapshot_id FOREIGN KEY (tariff_snapshot_id) REFERENCES core.tariff_snapshots(tariff_snapshot_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__requested_by_user_id FOREIGN KEY (requested_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__requested_by_service_identity_id FOREIGN KEY (requested_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__approved_by_user_id FOREIGN KEY (approved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT fk_coupon_applications__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__parent_policy_reference_id FOREIGN KEY (parent_policy_reference_id) REFERENCES discounts.discount_policy_references(discount_policy_reference_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__fallback_policy_reference_id FOREIGN KEY (fallback_policy_reference_id) REFERENCES discounts.discount_policy_references(discount_policy_reference_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__created_by_service_identity_i FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT fk_discount_policy_references__updated_by_service_identity_i FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__tariff_snapshot_id FOREIGN KEY (tariff_snapshot_id) REFERENCES core.tariff_snapshots(tariff_snapshot_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__evaluated_policy_referenc FOREIGN KEY (evaluated_policy_reference_id) REFERENCES discounts.discount_policy_references(discount_policy_reference_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__applied_policy_reference_ FOREIGN KEY (applied_policy_reference_id) REFERENCES discounts.discount_policy_references(discount_policy_reference_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__fallback_policy_reference FOREIGN KEY (fallback_policy_reference_id) REFERENCES discounts.discount_policy_references(discount_policy_reference_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__validated_by_user_id FOREIGN KEY (validated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__validated_by_service_iden FOREIGN KEY (validated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__requested_by_user_id FOREIGN KEY (requested_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__requested_by_service_iden FOREIGN KEY (requested_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__created_by_service_identi FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT fk_statutory_discount_validations__updated_by_service_identi FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__statutory_discount_validati FOREIGN KEY (statutory_discount_validation_id) REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__captured_by_user_id FOREIGN KEY (captured_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__captured_by_service_identit FOREIGN KEY (captured_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__purged_by_user_id FOREIGN KEY (purged_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__purged_by_service_identity_ FOREIGN KEY (purged_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__created_by_service_identity FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT fk_discount_evidence_references__updated_by_service_identity FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_devices ADD CONSTRAINT fk_gate_devices__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_devices ADD CONSTRAINT fk_gate_devices__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_devices ADD CONSTRAINT fk_gate_devices__service_identity_id FOREIGN KEY (service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_devices ADD CONSTRAINT fk_gate_devices__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_devices ADD CONSTRAINT fk_gate_devices__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_devices ADD CONSTRAINT fk_gate_devices__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_devices ADD CONSTRAINT fk_gate_devices__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_authorization_consumptions ADD CONSTRAINT fk_gate_authorization_consumptions__exit_authorization_id FOREIGN KEY (exit_authorization_id) REFERENCES core.exit_authorizations(exit_authorization_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_authorization_consumptions ADD CONSTRAINT fk_gate_authorization_consumptions__gate_device_id FOREIGN KEY (gate_device_id) REFERENCES gates.gate_devices(gate_device_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_authorization_consumptions ADD CONSTRAINT fk_gate_authorization_consumptions__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_authorization_consumptions ADD CONSTRAINT fk_gate_authorization_consumptions__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_authorization_consumptions ADD CONSTRAINT fk_gate_authorization_consumptions__created_by_service_ident FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_authorization_consumptions ADD CONSTRAINT fk_gate_authorization_consumptions__updated_by_service_ident FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_events ADD CONSTRAINT fk_gate_events__gate_device_id FOREIGN KEY (gate_device_id) REFERENCES gates.gate_devices(gate_device_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_events ADD CONSTRAINT fk_gate_events__gate_authorization_consumption_id FOREIGN KEY (gate_authorization_consumption_id) REFERENCES gates.gate_authorization_consumptions(gate_authorization_consumption_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_events ADD CONSTRAINT fk_gate_events__exit_authorization_id FOREIGN KEY (exit_authorization_id) REFERENCES core.exit_authorizations(exit_authorization_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_events ADD CONSTRAINT fk_gate_events__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_events ADD CONSTRAINT fk_gate_events__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_events ADD CONSTRAINT fk_gate_events__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_heartbeats ADD CONSTRAINT fk_gate_heartbeats__gate_device_id FOREIGN KEY (gate_device_id) REFERENCES gates.gate_devices(gate_device_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_heartbeats ADD CONSTRAINT fk_gate_heartbeats__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_heartbeats ADD CONSTRAINT fk_gate_heartbeats__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE gates.gate_heartbeats ADD CONSTRAINT fk_gate_heartbeats__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__exit_authorization_id FOREIGN KEY (exit_authorization_id) REFERENCES core.exit_authorizations(exit_authorization_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__gate_authorization_consumption_id FOREIGN KEY (gate_authorization_consumption_id) REFERENCES gates.gate_authorization_consumptions(gate_authorization_consumption_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__override_approval_id FOREIGN KEY (override_approval_id) REFERENCES operations.override_approvals(override_approval_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__gate_device_id FOREIGN KEY (gate_device_id) REFERENCES gates.gate_devices(gate_device_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__reconciliation_item_id FOREIGN KEY (reconciliation_item_id) REFERENCES reconciliation.reconciliation_items(reconciliation_item_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__performed_by_user_id FOREIGN KEY (performed_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__recorded_by_user_id FOREIGN KEY (recorded_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__recorded_by_service_identity_id FOREIGN KEY (recorded_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT fk_manual_gate_logs__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__requested_by_user_id FOREIGN KEY (requested_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_requests ADD CONSTRAINT fk_override_requests__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_approvals ADD CONSTRAINT fk_override_approvals__override_request_id FOREIGN KEY (override_request_id) REFERENCES operations.override_requests(override_request_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_approvals ADD CONSTRAINT fk_override_approvals__decided_by_user_id FOREIGN KEY (decided_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_approvals ADD CONSTRAINT fk_override_approvals__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.override_approvals ADD CONSTRAINT fk_override_approvals__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__gate_device_id FOREIGN KEY (gate_device_id) REFERENCES gates.gate_devices(gate_device_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__owner_user_id FOREIGN KEY (owner_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__owner_service_identity_id FOREIGN KEY (owner_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.incident_records ADD CONSTRAINT fk_incident_records__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.operator_action_logs ADD CONSTRAINT fk_operator_action_logs__operator_user_id FOREIGN KEY (operator_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.operator_action_logs ADD CONSTRAINT fk_operator_action_logs__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.operator_action_logs ADD CONSTRAINT fk_operator_action_logs__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.operator_action_logs ADD CONSTRAINT fk_operator_action_logs__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE operations.operator_action_logs ADD CONSTRAINT fk_operator_action_logs__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__parking_session_id FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__manual_gate_log_id FOREIGN KEY (manual_gate_log_id) REFERENCES operations.manual_gate_logs(manual_gate_log_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__captured_by_user_id FOREIGN KEY (captured_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__captured_by_service_identity_id FOREIGN KEY (captured_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__imported_by_service_identity_id FOREIGN KEY (imported_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT fk_mops_transaction_records__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__initiated_by_user_id FOREIGN KEY (initiated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__initiated_by_service_identity_id FOREIGN KEY (initiated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT fk_reconciliation_runs__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__reconciliation_run_id FOREIGN KEY (reconciliation_run_id) REFERENCES reconciliation.reconciliation_runs(reconciliation_run_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__mops_transaction_record_id FOREIGN KEY (mops_transaction_record_id) REFERENCES reconciliation.mops_transaction_records(mops_transaction_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__manual_gate_log_id FOREIGN KEY (manual_gate_log_id) REFERENCES operations.manual_gate_logs(manual_gate_log_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__payment_attempt_id FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__payment_confirmation_id FOREIGN KEY (payment_confirmation_id) REFERENCES core.payment_confirmations(payment_confirmation_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__provider_outcome_id FOREIGN KEY (provider_outcome_id) REFERENCES payments.provider_outcomes(provider_outcome_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__resolved_by_user_id FOREIGN KEY (resolved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__resolved_by_service_identity_id FOREIGN KEY (resolved_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT fk_reconciliation_items__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__reconciliation_run_id FOREIGN KEY (reconciliation_run_id) REFERENCES reconciliation.reconciliation_runs(reconciliation_run_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__reconciliation_item_id FOREIGN KEY (reconciliation_item_id) REFERENCES reconciliation.reconciliation_items(reconciliation_item_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__assigned_to_user_id FOREIGN KEY (assigned_to_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__assigned_to_service_identity_i FOREIGN KEY (assigned_to_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__resolved_by_user_id FOREIGN KEY (resolved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__resolved_by_service_identity_i FOREIGN KEY (resolved_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__closed_by_user_id FOREIGN KEY (closed_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__closed_by_service_identity_id FOREIGN KEY (closed_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT fk_reconciliation_exceptions__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__reconciliation_item_id FOREIGN KEY (reconciliation_item_id) REFERENCES reconciliation.reconciliation_items(reconciliation_item_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__mops_transaction_record_id FOREIGN KEY (mops_transaction_record_id) REFERENCES reconciliation.mops_transaction_records(mops_transaction_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__reconciliation_exception_i FOREIGN KEY (reconciliation_exception_id) REFERENCES reconciliation.reconciliation_exceptions(reconciliation_exception_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__payment_confirmation_id FOREIGN KEY (payment_confirmation_id) REFERENCES core.payment_confirmations(payment_confirmation_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__provider_outcome_id FOREIGN KEY (provider_outcome_id) REFERENCES payments.provider_outcomes(provider_outcome_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__compared_by_user_id FOREIGN KEY (compared_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__compared_by_service_identi FOREIGN KEY (compared_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT fk_settlement_comparison_records__created_by_service_identit FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.site_groups ADD CONSTRAINT fk_site_groups__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.site_groups ADD CONSTRAINT fk_site_groups__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.site_groups ADD CONSTRAINT fk_site_groups__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.site_groups ADD CONSTRAINT fk_site_groups__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.sites ADD CONSTRAINT fk_sites__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.sites ADD CONSTRAINT fk_sites__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.sites ADD CONSTRAINT fk_sites__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.sites ADD CONSTRAINT fk_sites__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.sites ADD CONSTRAINT fk_sites__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.lanes ADD CONSTRAINT fk_lanes__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.lanes ADD CONSTRAINT fk_lanes__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.lanes ADD CONSTRAINT fk_lanes__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.lanes ADD CONSTRAINT fk_lanes__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.lanes ADD CONSTRAINT fk_lanes__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__gate_device_id FOREIGN KEY (gate_device_id) REFERENCES gates.gate_devices(gate_device_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__service_identity_id FOREIGN KEY (service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__assigned_by_user_id FOREIGN KEY (assigned_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__assigned_by_service_identity_id FOREIGN KEY (assigned_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__unassigned_by_user_id FOREIGN KEY (unassigned_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__unassigned_by_service_identity_id FOREIGN KEY (unassigned_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE sites.device_assignments ADD CONSTRAINT fk_device_assignments__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchants ADD CONSTRAINT fk_merchants__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchants ADD CONSTRAINT fk_merchants__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchants ADD CONSTRAINT fk_merchants__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchants ADD CONSTRAINT fk_merchants__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__merchant_id FOREIGN KEY (merchant_id) REFERENCES merchants.merchants(merchant_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__approved_by_user_id FOREIGN KEY (approved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT fk_merchant_site_scopes__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT fk_merchant_wallets__merchant_id FOREIGN KEY (merchant_id) REFERENCES merchants.merchants(merchant_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT fk_merchant_wallets__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT fk_merchant_wallets__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT fk_merchant_wallets__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT fk_merchant_wallets__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_users ADD CONSTRAINT fk_merchant_users__merchant_id FOREIGN KEY (merchant_id) REFERENCES merchants.merchants(merchant_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_users ADD CONSTRAINT fk_merchant_users__user_id FOREIGN KEY (user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_users ADD CONSTRAINT fk_merchant_users__revoked_by_user_id FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_users ADD CONSTRAINT fk_merchant_users__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_users ADD CONSTRAINT fk_merchant_users__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_users ADD CONSTRAINT fk_merchant_users__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE merchants.merchant_users ADD CONSTRAINT fk_merchant_users__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.users ADD CONSTRAINT fk_users__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.users ADD CONSTRAINT fk_users__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.users ADD CONSTRAINT fk_users__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.users ADD CONSTRAINT fk_users__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.roles ADD CONSTRAINT fk_roles__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.roles ADD CONSTRAINT fk_roles__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.roles ADD CONSTRAINT fk_roles__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.roles ADD CONSTRAINT fk_roles__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.permissions ADD CONSTRAINT fk_permissions__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.permissions ADD CONSTRAINT fk_permissions__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.permissions ADD CONSTRAINT fk_permissions__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.permissions ADD CONSTRAINT fk_permissions__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__user_id FOREIGN KEY (user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__role_id FOREIGN KEY (role_id) REFERENCES identity.roles(role_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__assigned_by_user_id FOREIGN KEY (assigned_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__assigned_by_service_identity_id FOREIGN KEY (assigned_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__revoked_by_user_id FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__revoked_by_service_identity_id FOREIGN KEY (revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__last_reviewed_by_user_id FOREIGN KEY (last_reviewed_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.user_roles ADD CONSTRAINT fk_user_roles__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__role_id FOREIGN KEY (role_id) REFERENCES identity.roles(role_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__permission_id FOREIGN KEY (permission_id) REFERENCES identity.permissions(permission_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__assigned_by_user_id FOREIGN KEY (assigned_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__assigned_by_service_identity_id FOREIGN KEY (assigned_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__revoked_by_user_id FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__revoked_by_service_identity_id FOREIGN KEY (revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.role_permissions ADD CONSTRAINT fk_role_permissions__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.service_identities ADD CONSTRAINT fk_service_identities__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.service_identities ADD CONSTRAINT fk_service_identities__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.service_identities ADD CONSTRAINT fk_service_identities__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE identity.service_identities ADD CONSTRAINT fk_service_identities__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.audit_events ADD CONSTRAINT fk_audit_events__actor_user_id FOREIGN KEY (actor_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.audit_events ADD CONSTRAINT fk_audit_events__actor_service_identity_id FOREIGN KEY (actor_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.audit_events ADD CONSTRAINT fk_audit_events__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.audit_trail_entries ADD CONSTRAINT fk_audit_trail_entries__audit_event_id FOREIGN KEY (audit_event_id) REFERENCES audit.audit_events(audit_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.audit_trail_entries ADD CONSTRAINT fk_audit_trail_entries__changed_by_user_id FOREIGN KEY (changed_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.audit_trail_entries ADD CONSTRAINT fk_audit_trail_entries__changed_by_service_identity_id FOREIGN KEY (changed_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.audit_trail_entries ADD CONSTRAINT fk_audit_trail_entries__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.security_events ADD CONSTRAINT fk_security_events__audit_event_id FOREIGN KEY (audit_event_id) REFERENCES audit.audit_events(audit_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.security_events ADD CONSTRAINT fk_security_events__actor_user_id FOREIGN KEY (actor_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.security_events ADD CONSTRAINT fk_security_events__actor_service_identity_id FOREIGN KEY (actor_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.security_events ADD CONSTRAINT fk_security_events__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.security_events ADD CONSTRAINT fk_security_events__resolved_by_user_id FOREIGN KEY (resolved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.security_events ADD CONSTRAINT fk_security_events__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__audit_event_id FOREIGN KEY (audit_event_id) REFERENCES audit.audit_events(audit_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__security_event_id FOREIGN KEY (security_event_id) REFERENCES audit.security_events(security_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__linked_by_user_id FOREIGN KEY (linked_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__linked_by_service_identity_id FOREIGN KEY (linked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__purged_by_user_id FOREIGN KEY (purged_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__purged_by_service_identity_id FOREIGN KEY (purged_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE audit.evidence_links ADD CONSTRAINT fk_evidence_links__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_systems ADD CONSTRAINT fk_vendor_systems__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_systems ADD CONSTRAINT fk_vendor_systems__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_systems ADD CONSTRAINT fk_vendor_systems__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_systems ADD CONSTRAINT fk_vendor_systems__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT fk_vendor_endpoints__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT fk_vendor_endpoints__credential_reference_id FOREIGN KEY (credential_reference_id) REFERENCES integration.integration_credential_references(integration_credential_reference_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT fk_vendor_endpoints__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT fk_vendor_endpoints__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT fk_vendor_endpoints__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT fk_vendor_endpoints__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__lane_id FOREIGN KEY (lane_id) REFERENCES sites.lanes(lane_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__gate_device_id FOREIGN KEY (gate_device_id) REFERENCES gates.gate_devices(gate_device_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT fk_adapter_mappings__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT fk_integration_credential_references__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT fk_integration_credential_references__service_identity_id FOREIGN KEY (service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT fk_integration_credential_references__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT fk_integration_credential_references__created_by_service_ide FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT fk_integration_credential_references__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT fk_integration_credential_references__updated_by_service_ide FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_health_records ADD CONSTRAINT fk_integration_health_records__vendor_system_id FOREIGN KEY (vendor_system_id) REFERENCES integration.vendor_systems(vendor_system_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_health_records ADD CONSTRAINT fk_integration_health_records__vendor_endpoint_id FOREIGN KEY (vendor_endpoint_id) REFERENCES integration.vendor_endpoints(vendor_endpoint_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_health_records ADD CONSTRAINT fk_integration_health_records__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_health_records ADD CONSTRAINT fk_integration_health_records__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_health_records ADD CONSTRAINT fk_integration_health_records__incident_record_id FOREIGN KEY (incident_record_id) REFERENCES operations.incident_records(incident_record_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE integration.integration_health_records ADD CONSTRAINT fk_integration_health_records__observed_by_service_identity_ FOREIGN KEY (observed_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.system_parameters ADD CONSTRAINT fk_system_parameters__approved_by_user_id FOREIGN KEY (approved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.system_parameters ADD CONSTRAINT fk_system_parameters__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.system_parameters ADD CONSTRAINT fk_system_parameters__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.system_parameters ADD CONSTRAINT fk_system_parameters__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.system_parameters ADD CONSTRAINT fk_system_parameters__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__site_group_id FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__site_id FOREIGN KEY (site_id) REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__merchant_id FOREIGN KEY (merchant_id) REFERENCES merchants.merchants(merchant_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__payment_rail_id FOREIGN KEY (payment_rail_id) REFERENCES payments.payment_rails(payment_rail_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__service_identity_id FOREIGN KEY (service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__approved_by_user_id FOREIGN KEY (approved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.feature_flags ADD CONSTRAINT fk_feature_flags__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT fk_rate_limit_policies__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT fk_rate_limit_policies__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT fk_rate_limit_policies__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT fk_rate_limit_policies__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.ttl_policies ADD CONSTRAINT fk_ttl_policies__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.ttl_policies ADD CONSTRAINT fk_ttl_policies__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.ttl_policies ADD CONSTRAINT fk_ttl_policies__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.ttl_policies ADD CONSTRAINT fk_ttl_policies__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT fk_controlled_code_sets__created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT fk_controlled_code_sets__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT fk_controlled_code_sets__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT fk_controlled_code_sets__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.domain_events ADD CONSTRAINT fk_domain_events__actor_user_id FOREIGN KEY (actor_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.domain_events ADD CONSTRAINT fk_domain_events__actor_service_identity_id FOREIGN KEY (actor_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.domain_events ADD CONSTRAINT fk_domain_events__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.outbox_events ADD CONSTRAINT fk_outbox_events__domain_event_id FOREIGN KEY (domain_event_id) REFERENCES events.domain_events(domain_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.outbox_events ADD CONSTRAINT fk_outbox_events__locked_by_service_identity_id FOREIGN KEY (locked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.outbox_events ADD CONSTRAINT fk_outbox_events__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.outbox_events ADD CONSTRAINT fk_outbox_events__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.event_publications ADD CONSTRAINT fk_event_publications__outbox_event_id FOREIGN KEY (outbox_event_id) REFERENCES events.outbox_events(outbox_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.event_publications ADD CONSTRAINT fk_event_publications__publisher_service_identity_id FOREIGN KEY (publisher_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__outbox_event_id FOREIGN KEY (outbox_event_id) REFERENCES events.outbox_events(outbox_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__event_publication_id FOREIGN KEY (event_publication_id) REFERENCES events.event_publications(event_publication_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__resolved_by_user_id FOREIGN KEY (resolved_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__resolved_by_service_identity_id FOREIGN KEY (resolved_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__replay_requested_by_user_id FOREIGN KEY (replay_requested_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__replay_requested_by_service_identity FOREIGN KEY (replay_requested_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.dead_letter_records ADD CONSTRAINT fk_dead_letter_records__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.consumer_checkpoints ADD CONSTRAINT fk_consumer_checkpoints__last_outbox_event_id FOREIGN KEY (last_outbox_event_id) REFERENCES events.outbox_events(outbox_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.consumer_checkpoints ADD CONSTRAINT fk_consumer_checkpoints__last_domain_event_id FOREIGN KEY (last_domain_event_id) REFERENCES events.domain_events(domain_event_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.consumer_checkpoints ADD CONSTRAINT fk_consumer_checkpoints__locked_by_service_identity_id FOREIGN KEY (locked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;
ALTER TABLE events.consumer_checkpoints ADD CONSTRAINT fk_consumer_checkpoints__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE;

-- ============================================================
-- CHECK CONSTRAINTS
-- ============================================================
ALTER TABLE core.parking_sessions ADD CONSTRAINT ck_parking_sessions__row_version_positive CHECK (row_version > 0);
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT ck_tariff_snapshots__row_version_positive CHECK (row_version > 0);
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT ck_tariff_snapshots__gross_amount_non_negative CHECK (gross_amount IS NULL OR gross_amount >= 0);
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT ck_tariff_snapshots__statutory_discount_amount_non_negative CHECK (statutory_discount_amount IS NULL OR statutory_discount_amount >= 0);
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT ck_tariff_snapshots__coupon_discount_amount_non_negative CHECK (coupon_discount_amount IS NULL OR coupon_discount_amount >= 0);
ALTER TABLE core.tariff_snapshots ADD CONSTRAINT ck_tariff_snapshots__net_amount_non_negative CHECK (net_amount IS NULL OR net_amount >= 0);
ALTER TABLE core.payment_attempts ADD CONSTRAINT ck_payment_attempts__row_version_positive CHECK (row_version > 0);
ALTER TABLE core.payment_attempts ADD CONSTRAINT ck_payment_attempts__amount_non_negative CHECK (amount IS NULL OR amount >= 0);
ALTER TABLE core.payment_confirmations ADD CONSTRAINT ck_payment_confirmations__confirmed_amount_non_negative CHECK (confirmed_amount IS NULL OR confirmed_amount >= 0);
ALTER TABLE core.exit_authorizations ADD CONSTRAINT ck_exit_authorizations__row_version_positive CHECK (row_version > 0);
ALTER TABLE payments.payment_rails ADD CONSTRAINT ck_payment_rails__row_version_positive CHECK (row_version > 0);
ALTER TABLE payments.payment_rails ADD CONSTRAINT ck_payment_rails__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE payments.provider_sessions ADD CONSTRAINT ck_provider_sessions__row_version_positive CHECK (row_version > 0);
ALTER TABLE payments.provider_sessions ADD CONSTRAINT ck_provider_sessions__amount_non_negative CHECK (amount IS NULL OR amount >= 0);
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT ck_provider_outcomes__row_version_positive CHECK (row_version > 0);
ALTER TABLE payments.provider_outcomes ADD CONSTRAINT ck_provider_outcomes__amount_non_negative CHECK (amount IS NULL OR amount >= 0);
ALTER TABLE sessions.session_resolution_requests ADD CONSTRAINT ck_session_resolution_requests__row_version_positive CHECK (row_version > 0);
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT ck_session_resolution_results__match_count_non_negative CHECK (match_count IS NULL OR match_count >= 0);
ALTER TABLE sessions.session_lookup_cache ADD CONSTRAINT ck_session_lookup_cache__row_version_positive CHECK (row_version > 0);
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT ck_session_identifier_indexes__row_version_positive CHECK (row_version > 0);
ALTER TABLE sessions.session_identifier_indexes ADD CONSTRAINT ck_session_identifier_indexes__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE coupons.coupons ADD CONSTRAINT ck_coupons__row_version_positive CHECK (row_version > 0);
ALTER TABLE coupons.coupons ADD CONSTRAINT ck_coupons__denomination_value_non_negative CHECK (denomination_value IS NULL OR denomination_value >= 0);
ALTER TABLE coupons.coupons ADD CONSTRAINT ck_coupons__maximum_discount_amount_non_negative CHECK (maximum_discount_amount IS NULL OR maximum_discount_amount >= 0);
ALTER TABLE coupons.coupons ADD CONSTRAINT ck_coupons__minimum_gross_amount_non_negative CHECK (minimum_gross_amount IS NULL OR minimum_gross_amount >= 0);
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT ck_coupon_rule_groups__row_version_positive CHECK (row_version > 0);
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT ck_coupon_rule_groups__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT ck_coupon_rule_groups__evaluation_priority_non_negative CHECK (evaluation_priority IS NULL OR evaluation_priority >= 0);
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT ck_coupon_rules__row_version_positive CHECK (row_version > 0);
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT ck_coupon_rules__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT ck_coupon_applications__row_version_positive CHECK (row_version > 0);
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT ck_coupon_applications__gross_amount_at_application_non_nega CHECK (gross_amount_at_application IS NULL OR gross_amount_at_application >= 0);
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT ck_coupon_applications__coupon_discount_amount_non_negative CHECK (coupon_discount_amount IS NULL OR coupon_discount_amount >= 0);
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT ck_coupon_applications__net_amount_after_coupon_non_negative CHECK (net_amount_after_coupon IS NULL OR net_amount_after_coupon >= 0);
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT ck_discount_policy_references__row_version_positive CHECK (row_version > 0);
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT ck_discount_policy_references__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT ck_discount_policy_references__precedence_rank_non_negative CHECK (precedence_rank IS NULL OR precedence_rank >= 0);
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT ck_statutory_discount_validations__row_version_positive CHECK (row_version > 0);
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT ck_statutory_discount_validations__gross_amount_at_validatio CHECK (gross_amount_at_validation IS NULL OR gross_amount_at_validation >= 0);
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT ck_statutory_discount_validations__statutory_discount_amount CHECK (statutory_discount_amount IS NULL OR statutory_discount_amount >= 0);
ALTER TABLE discounts.statutory_discount_validations ADD CONSTRAINT ck_statutory_discount_validations__net_amount_after_discount CHECK (net_amount_after_discount IS NULL OR net_amount_after_discount >= 0);
ALTER TABLE discounts.discount_evidence_references ADD CONSTRAINT ck_discount_evidence_references__row_version_positive CHECK (row_version > 0);
ALTER TABLE gates.gate_devices ADD CONSTRAINT ck_gate_devices__row_version_positive CHECK (row_version > 0);
ALTER TABLE gates.gate_authorization_consumptions ADD CONSTRAINT ck_gate_authorization_consumptions__row_version_positive CHECK (row_version > 0);
ALTER TABLE gates.gate_heartbeats ADD CONSTRAINT ck_gate_heartbeats__latency_ms_non_negative CHECK (latency_ms IS NULL OR latency_ms >= 0);
ALTER TABLE operations.manual_gate_logs ADD CONSTRAINT ck_manual_gate_logs__row_version_positive CHECK (row_version > 0);
ALTER TABLE operations.override_requests ADD CONSTRAINT ck_override_requests__row_version_positive CHECK (row_version > 0);
ALTER TABLE operations.incident_records ADD CONSTRAINT ck_incident_records__row_version_positive CHECK (row_version > 0);
ALTER TABLE operations.incident_records ADD CONSTRAINT ck_incident_records__resolved_after_detected CHECK (resolved_at IS NULL OR resolved_at >= detected_at);
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT ck_mops_transaction_records__row_version_positive CHECK (row_version > 0);
ALTER TABLE reconciliation.mops_transaction_records ADD CONSTRAINT ck_mops_transaction_records__amount_non_negative CHECK (amount IS NULL OR amount >= 0);
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT ck_reconciliation_runs__row_version_positive CHECK (row_version > 0);
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT ck_reconciliation_runs__completed_after_started CHECK (completed_at IS NULL OR completed_at >= started_at);
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT ck_reconciliation_runs__item_count_non_negative CHECK (item_count IS NULL OR item_count >= 0);
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT ck_reconciliation_runs__matched_count_non_negative CHECK (matched_count IS NULL OR matched_count >= 0);
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT ck_reconciliation_runs__exception_count_non_negative CHECK (exception_count IS NULL OR exception_count >= 0);
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT ck_reconciliation_runs__rejected_count_non_negative CHECK (rejected_count IS NULL OR rejected_count >= 0);
ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT ck_reconciliation_runs__disputed_count_non_negative CHECK (disputed_count IS NULL OR disputed_count >= 0);
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT ck_reconciliation_items__row_version_positive CHECK (row_version > 0);
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT ck_reconciliation_items__expected_amount_non_negative CHECK (expected_amount IS NULL OR expected_amount >= 0);
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT ck_reconciliation_items__actual_amount_non_negative CHECK (actual_amount IS NULL OR actual_amount >= 0);
ALTER TABLE reconciliation.reconciliation_items ADD CONSTRAINT ck_reconciliation_items__variance_amount_non_negative CHECK (variance_amount IS NULL OR variance_amount >= 0);
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT ck_reconciliation_exceptions__row_version_positive CHECK (row_version > 0);
ALTER TABLE reconciliation.reconciliation_exceptions ADD CONSTRAINT ck_reconciliation_exceptions__resolved_after_detected CHECK (resolved_at IS NULL OR resolved_at >= detected_at);
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT ck_settlement_comparison_records__expected_amount_non_negati CHECK (expected_amount IS NULL OR expected_amount >= 0);
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT ck_settlement_comparison_records__actual_amount_non_negative CHECK (actual_amount IS NULL OR actual_amount >= 0);
ALTER TABLE reconciliation.settlement_comparison_records ADD CONSTRAINT ck_settlement_comparison_records__variance_amount_non_negati CHECK (variance_amount IS NULL OR variance_amount >= 0);
ALTER TABLE sites.site_groups ADD CONSTRAINT ck_site_groups__row_version_positive CHECK (row_version > 0);
ALTER TABLE sites.site_groups ADD CONSTRAINT ck_site_groups__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE sites.sites ADD CONSTRAINT ck_sites__row_version_positive CHECK (row_version > 0);
ALTER TABLE sites.sites ADD CONSTRAINT ck_sites__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE sites.lanes ADD CONSTRAINT ck_lanes__row_version_positive CHECK (row_version > 0);
ALTER TABLE sites.lanes ADD CONSTRAINT ck_lanes__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE sites.lanes ADD CONSTRAINT ck_lanes__display_order_non_negative CHECK (display_order IS NULL OR display_order >= 0);
ALTER TABLE sites.device_assignments ADD CONSTRAINT ck_device_assignments__row_version_positive CHECK (row_version > 0);
ALTER TABLE merchants.merchants ADD CONSTRAINT ck_merchants__row_version_positive CHECK (row_version > 0);
ALTER TABLE merchants.merchants ADD CONSTRAINT ck_merchants__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT ck_merchant_site_scopes__row_version_positive CHECK (row_version > 0);
ALTER TABLE merchants.merchant_site_scopes ADD CONSTRAINT ck_merchant_site_scopes__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT ck_merchant_wallets__row_version_positive CHECK (row_version > 0);
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT ck_merchant_wallets__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT ck_merchant_wallets__available_balance_non_negative CHECK (available_balance IS NULL OR available_balance >= 0);
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT ck_merchant_wallets__reserved_balance_non_negative CHECK (reserved_balance IS NULL OR reserved_balance >= 0);
ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT ck_merchant_wallets__committed_balance_non_negative CHECK (committed_balance IS NULL OR committed_balance >= 0);
ALTER TABLE merchants.merchant_users ADD CONSTRAINT ck_merchant_users__row_version_positive CHECK (row_version > 0);
ALTER TABLE merchants.merchant_users ADD CONSTRAINT ck_merchant_users__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE identity.users ADD CONSTRAINT ck_users__row_version_positive CHECK (row_version > 0);
ALTER TABLE identity.users ADD CONSTRAINT ck_users__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE identity.roles ADD CONSTRAINT ck_roles__row_version_positive CHECK (row_version > 0);
ALTER TABLE identity.roles ADD CONSTRAINT ck_roles__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE identity.permissions ADD CONSTRAINT ck_permissions__row_version_positive CHECK (row_version > 0);
ALTER TABLE identity.user_roles ADD CONSTRAINT ck_user_roles__row_version_positive CHECK (row_version > 0);
ALTER TABLE identity.user_roles ADD CONSTRAINT ck_user_roles__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE identity.role_permissions ADD CONSTRAINT ck_role_permissions__row_version_positive CHECK (row_version > 0);
ALTER TABLE identity.role_permissions ADD CONSTRAINT ck_role_permissions__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE identity.service_identities ADD CONSTRAINT ck_service_identities__row_version_positive CHECK (row_version > 0);
ALTER TABLE identity.service_identities ADD CONSTRAINT ck_service_identities__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE audit.security_events ADD CONSTRAINT ck_security_events__row_version_positive CHECK (row_version > 0);
ALTER TABLE audit.security_events ADD CONSTRAINT ck_security_events__resolved_after_detected CHECK (resolved_at IS NULL OR resolved_at >= detected_at);
ALTER TABLE audit.evidence_links ADD CONSTRAINT ck_evidence_links__row_version_positive CHECK (row_version > 0);
ALTER TABLE integration.vendor_systems ADD CONSTRAINT ck_vendor_systems__row_version_positive CHECK (row_version > 0);
ALTER TABLE integration.vendor_systems ADD CONSTRAINT ck_vendor_systems__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT ck_vendor_endpoints__row_version_positive CHECK (row_version > 0);
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT ck_vendor_endpoints__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT ck_adapter_mappings__row_version_positive CHECK (row_version > 0);
ALTER TABLE integration.adapter_mappings ADD CONSTRAINT ck_adapter_mappings__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT ck_integration_credential_references__row_version_positive CHECK (row_version > 0);
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT ck_integration_credential_references__expires_after_rotation CHECK (expires_at IS NULL OR last_rotated_at IS NULL OR expires_at > last_rotated_at);
ALTER TABLE integration.integration_health_records ADD CONSTRAINT ck_integration_health_records__latency_ms_non_negative CHECK (latency_ms IS NULL OR latency_ms >= 0);
ALTER TABLE config.system_parameters ADD CONSTRAINT ck_system_parameters__row_version_positive CHECK (row_version > 0);
ALTER TABLE config.system_parameters ADD CONSTRAINT ck_system_parameters__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE config.feature_flags ADD CONSTRAINT ck_feature_flags__row_version_positive CHECK (row_version > 0);
ALTER TABLE config.feature_flags ADD CONSTRAINT ck_feature_flags__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT ck_rate_limit_policies__row_version_positive CHECK (row_version > 0);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT ck_rate_limit_policies__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT ck_rate_limit_policies__window_seconds_non_negative CHECK (window_seconds IS NULL OR window_seconds >= 0);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT ck_rate_limit_policies__window_seconds_positive CHECK (window_seconds > 0);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT ck_rate_limit_policies__max_requests_positive CHECK (max_requests > 0);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT ck_rate_limit_policies__burst_limit_non_negative CHECK (burst_limit IS NULL OR burst_limit >= 0);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT ck_rate_limit_policies__penalty_seconds_non_negative CHECK (penalty_seconds IS NULL OR penalty_seconds >= 0);
ALTER TABLE config.ttl_policies ADD CONSTRAINT ck_ttl_policies__row_version_positive CHECK (row_version > 0);
ALTER TABLE config.ttl_policies ADD CONSTRAINT ck_ttl_policies__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE config.ttl_policies ADD CONSTRAINT ck_ttl_policies__ttl_seconds_non_negative CHECK (ttl_seconds IS NULL OR ttl_seconds >= 0);
ALTER TABLE config.ttl_policies ADD CONSTRAINT ck_ttl_policies__ttl_seconds_positive CHECK (ttl_seconds > 0);
ALTER TABLE config.ttl_policies ADD CONSTRAINT ck_ttl_policies__grace_period_seconds_non_negative CHECK (grace_period_seconds IS NULL OR grace_period_seconds >= 0);
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT ck_controlled_code_sets__row_version_positive CHECK (row_version > 0);
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT ck_controlled_code_sets__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from);
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT ck_controlled_code_sets__sort_order_non_negative CHECK (sort_order IS NULL OR sort_order >= 0);
ALTER TABLE events.domain_events ADD CONSTRAINT ck_domain_events__event_version_positive CHECK (event_version > 0);
ALTER TABLE events.outbox_events ADD CONSTRAINT ck_outbox_events__row_version_positive CHECK (row_version > 0);
ALTER TABLE events.outbox_events ADD CONSTRAINT ck_outbox_events__event_version_positive CHECK (event_version > 0);
ALTER TABLE events.outbox_events ADD CONSTRAINT ck_outbox_events__retry_count_non_negative CHECK (retry_count IS NULL OR retry_count >= 0);
ALTER TABLE events.outbox_events ADD CONSTRAINT ck_outbox_events__max_retry_count_non_negative CHECK (max_retry_count IS NULL OR max_retry_count >= 0);
ALTER TABLE events.event_publications ADD CONSTRAINT ck_event_publications__completed_after_started CHECK (completed_at IS NULL OR completed_at >= started_at);
ALTER TABLE events.event_publications ADD CONSTRAINT ck_event_publications__duration_ms_non_negative CHECK (duration_ms IS NULL OR duration_ms >= 0);
ALTER TABLE events.dead_letter_records ADD CONSTRAINT ck_dead_letter_records__row_version_positive CHECK (row_version > 0);
ALTER TABLE events.consumer_checkpoints ADD CONSTRAINT ck_consumer_checkpoints__row_version_positive CHECK (row_version > 0);
ALTER TABLE events.consumer_checkpoints ADD CONSTRAINT ck_consumer_checkpoints__processed_count_non_negative CHECK (processed_count IS NULL OR processed_count >= 0);
ALTER TABLE events.consumer_checkpoints ADD CONSTRAINT ck_consumer_checkpoints__failure_count_non_negative CHECK (failure_count IS NULL OR failure_count >= 0);

-- ============================================================
-- UNIQUE CONSTRAINTS AND PARTIAL UNIQUE INDEXES
-- ============================================================
-- v1.2 constraint cleanup:
-- 1. Keep semantic uniqueness that enforces canonical control, idempotency, replay protection, and reference identity.
-- 2. Remove generated uniqueness on reason-code, status, currency-only, and append-oriented evidence fields.
-- 3. Use partial unique indexes where lifecycle state, NULL handling, or active-record semantics matter.

-- core.tariff_snapshots
CREATE UNIQUE INDEX IF NOT EXISTS ux_tariff_snapshots__active_by_session
ON core.tariff_snapshots (parking_session_id)
WHERE snapshot_status = 'ACTIVE';

CREATE UNIQUE INDEX IF NOT EXISTS ux_tariff_snapshots__superseded_by
ON core.tariff_snapshots (superseded_by_tariff_snapshot_id)
WHERE superseded_by_tariff_snapshot_id IS NOT NULL;

-- core.payment_attempts
ALTER TABLE core.payment_attempts ADD CONSTRAINT uq_payment_attempts__idempotency_key UNIQUE (idempotency_key);
ALTER TABLE core.payment_attempts ADD CONSTRAINT uq_payment_attempts__tariff_snapshot UNIQUE (tariff_snapshot_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_attempts__active_by_session
ON core.payment_attempts (parking_session_id)
WHERE attempt_status IN ('REQUESTED', 'PENDING_PROVIDER', 'PENDING_FINALIZATION');

-- core.payment_confirmations
ALTER TABLE core.payment_confirmations ADD CONSTRAINT uq_payment_confirmations__payment_attempt UNIQUE (payment_attempt_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_confirmations__provider_transaction_ref
ON core.payment_confirmations (payment_rail_id, provider_transaction_ref)
WHERE provider_transaction_ref IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_confirmations__provider_outcome
ON core.payment_confirmations (provider_outcome_id)
WHERE provider_outcome_id IS NOT NULL;

-- core.exit_authorizations
ALTER TABLE core.exit_authorizations ADD CONSTRAINT uq_exit_authorizations__payment_attempt UNIQUE (payment_attempt_id);
ALTER TABLE core.exit_authorizations ADD CONSTRAINT uq_exit_authorizations__payment_confirmation UNIQUE (payment_confirmation_id);
ALTER TABLE core.exit_authorizations ADD CONSTRAINT uq_exit_authorizations__token_hash UNIQUE (authorization_token_hash);

CREATE UNIQUE INDEX IF NOT EXISTS ux_exit_authorizations__active_by_session
ON core.exit_authorizations (parking_session_id)
WHERE authorization_status = 'ISSUED';

-- payments.payment_rails
ALTER TABLE payments.payment_rails ADD CONSTRAINT uq_payment_rails__rail_code UNIQUE (rail_code);

-- payments.provider_sessions
ALTER TABLE payments.provider_sessions ADD CONSTRAINT uq_provider_sessions__idempotency_key UNIQUE (idempotency_key);

CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_sessions__provider_ref
ON payments.provider_sessions (payment_rail_id, provider_session_ref)
WHERE provider_session_ref IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_sessions__active_by_attempt_rail
ON payments.provider_sessions (payment_attempt_id, payment_rail_id)
WHERE session_status IN ('CREATED', 'ACTIVE', 'PENDING');

-- payments.provider_callbacks
CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_callbacks__provider_event
ON payments.provider_callbacks (payment_rail_id, provider_event_ref)
WHERE provider_event_ref IS NOT NULL;

ALTER TABLE payments.provider_callbacks ADD CONSTRAINT uq_provider_callbacks__payload_hash UNIQUE (payment_rail_id, payload_hash);

-- payments.provider_status_queries
-- No hard uniqueness. Provider status queries may legitimately repeat under controlled retry policy.

-- payments.provider_outcomes
CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_outcomes__provider_transaction_ref
ON payments.provider_outcomes (payment_rail_id, provider_transaction_ref)
WHERE provider_transaction_ref IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_outcomes__provider_callback
ON payments.provider_outcomes (provider_callback_id)
WHERE provider_callback_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_outcomes__provider_status_query
ON payments.provider_outcomes (provider_status_query_id)
WHERE provider_status_query_id IS NOT NULL;

-- sessions.session_resolution_requests
CREATE UNIQUE INDEX IF NOT EXISTS ux_session_resolution_requests__idempotency_key
ON sessions.session_resolution_requests (idempotency_key)
WHERE idempotency_key IS NOT NULL;

-- sessions.session_resolution_results
ALTER TABLE sessions.session_resolution_results ADD CONSTRAINT uq_session_resolution_results__request UNIQUE (session_resolution_request_id);

-- sessions.session_lookup_cache
CREATE UNIQUE INDEX IF NOT EXISTS ux_session_lookup_cache__active_scope_without_site
ON sessions.session_lookup_cache (site_group_id, lookup_type, lookup_identifier_hash)
WHERE cache_status = 'ACTIVE' AND site_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_session_lookup_cache__active_scope_with_site
ON sessions.session_lookup_cache (site_group_id, site_id, lookup_type, lookup_identifier_hash)
WHERE cache_status = 'ACTIVE' AND site_id IS NOT NULL;

-- sessions.session_identifier_indexes
CREATE UNIQUE INDEX IF NOT EXISTS ux_session_identifier_indexes__active_scope_without_site
ON sessions.session_identifier_indexes (site_group_id, identifier_type, identifier_hash)
WHERE identifier_status = 'ACTIVE' AND site_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_session_identifier_indexes__active_scope_with_site
ON sessions.session_identifier_indexes (site_group_id, site_id, identifier_type, identifier_hash)
WHERE identifier_status = 'ACTIVE' AND site_id IS NOT NULL;

-- coupons.coupons, coupon_rule_groups, coupon_rules, coupon_applications
ALTER TABLE coupons.coupons ADD CONSTRAINT uq_coupons__merchant_coupon_code UNIQUE (merchant_id, coupon_code);
ALTER TABLE coupons.coupon_rule_groups ADD CONSTRAINT uq_coupon_rule_groups__coupon_rule_group_code UNIQUE (coupon_id, rule_group_code);
ALTER TABLE coupons.coupon_rules ADD CONSTRAINT uq_coupon_rules__group_rule_code UNIQUE (coupon_rule_group_id, rule_code);
ALTER TABLE coupons.coupon_applications ADD CONSTRAINT uq_coupon_applications__idempotency_key UNIQUE (idempotency_key);

CREATE UNIQUE INDEX IF NOT EXISTS ux_coupon_applications__active_merchant_session
ON coupons.coupon_applications (merchant_id, parking_session_id)
WHERE application_status IN ('REQUESTED', 'RESERVED', 'APPLIED');

CREATE UNIQUE INDEX IF NOT EXISTS ux_coupon_applications__reservation_ref
ON coupons.coupon_applications (reservation_ref)
WHERE reservation_ref IS NOT NULL;

-- discounts.discount_policy_references, statutory_discount_validations, discount_evidence_references
ALTER TABLE discounts.discount_policy_references ADD CONSTRAINT uq_discount_policy_references__policy_code_version UNIQUE (policy_code, policy_version);

CREATE UNIQUE INDEX IF NOT EXISTS ux_discount_policy_references__active_local_policy
ON discounts.discount_policy_references (entitlement_type, lgu_code, site_group_id, site_id, policy_level, policy_version)
WHERE policy_status = 'ACTIVE' AND lgu_code IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_statutory_discount_validations__active_session_entitlement
ON discounts.statutory_discount_validations (parking_session_id, entitlement_type)
WHERE validation_status IN ('REQUESTED', 'PENDING_OPERATOR_REVIEW', 'APPROVED');

CREATE UNIQUE INDEX IF NOT EXISTS ux_discount_evidence_references__evidence_hash
ON discounts.discount_evidence_references (evidence_hash)
WHERE evidence_hash IS NOT NULL;

-- gates.gate_devices, gate_authorization_consumptions, gate_events, gate_heartbeats
ALTER TABLE gates.gate_devices ADD CONSTRAINT uq_gate_devices__site_device_code UNIQUE (site_id, device_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_gate_devices__service_identity
ON gates.gate_devices (service_identity_id)
WHERE service_identity_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_gate_devices__serial_number
ON gates.gate_devices (serial_number)
WHERE serial_number IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_gate_devices__vendor_device_ref
ON gates.gate_devices (site_id, vendor_device_ref)
WHERE vendor_device_ref IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_gate_auth_consumptions__successful_exit_auth
ON gates.gate_authorization_consumptions (exit_authorization_id)
WHERE consume_status = 'CONSUMED';

-- Gate events and heartbeats are append-oriented evidence. No broad hard uniqueness is applied.

-- operations.manual_gate_logs, override_requests, override_approvals, incident_records, operator_action_logs
ALTER TABLE operations.incident_records ADD CONSTRAINT uq_incident_records__site_group_incident_code UNIQUE (site_group_id, incident_code);
ALTER TABLE operations.override_approvals ADD CONSTRAINT uq_override_approvals__request_sequence UNIQUE (override_request_id, approval_sequence);

-- Manual gate logs and operator action logs are append-oriented operational evidence. No broad hard uniqueness is applied.

-- reconciliation.mops_transaction_records, reconciliation_runs, reconciliation_items, reconciliation_exceptions, settlement_comparison_records
CREATE UNIQUE INDEX IF NOT EXISTS ux_mops_transaction_records__source_transaction_ref
ON reconciliation.mops_transaction_records (source_system_code, source_transaction_ref)
WHERE source_transaction_ref IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_mops_transaction_records__source_batch_collection
ON reconciliation.mops_transaction_records (source_system_code, source_batch_ref, collection_reference)
WHERE source_batch_ref IS NOT NULL AND collection_reference IS NOT NULL;

ALTER TABLE reconciliation.reconciliation_runs ADD CONSTRAINT uq_reconciliation_runs__run_code UNIQUE (run_code);

-- Reconciliation items, exceptions, and settlement comparison records are review/evidence records. No broad reason-code uniqueness is applied.

-- sites.site_groups, sites, lanes, device_assignments
ALTER TABLE sites.site_groups ADD CONSTRAINT uq_site_groups__site_group_code UNIQUE (site_group_code);
ALTER TABLE sites.sites ADD CONSTRAINT uq_sites__site_group_site_code UNIQUE (site_group_id, site_code);
ALTER TABLE sites.lanes ADD CONSTRAINT uq_lanes__site_lane_code UNIQUE (site_id, lane_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_device_assignments__active_gate_device
ON sites.device_assignments (gate_device_id)
WHERE assignment_status = 'ACTIVE' AND gate_device_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_device_assignments__active_service_identity
ON sites.device_assignments (service_identity_id)
WHERE assignment_status = 'ACTIVE' AND service_identity_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_device_assignments__active_lane_assignment_type
ON sites.device_assignments (site_id, lane_id, assignment_type)
WHERE assignment_status = 'ACTIVE' AND lane_id IS NOT NULL;

-- merchants.merchants, merchant_site_scopes, merchant_wallets, merchant_users
ALTER TABLE merchants.merchants ADD CONSTRAINT uq_merchants__merchant_code UNIQUE (merchant_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_merchant_site_scopes__active_site_group_scope
ON merchants.merchant_site_scopes (merchant_id, site_group_id, scope_type)
WHERE scope_status = 'ACTIVE' AND site_group_id IS NOT NULL AND site_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_merchant_site_scopes__active_site_scope
ON merchants.merchant_site_scopes (merchant_id, site_id, scope_type)
WHERE scope_status = 'ACTIVE' AND site_id IS NOT NULL;

ALTER TABLE merchants.merchant_wallets ADD CONSTRAINT uq_merchant_wallets__merchant_wallet_code UNIQUE (merchant_id, wallet_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_merchant_wallets__active_currency_type
ON merchants.merchant_wallets (merchant_id, currency_code, wallet_type)
WHERE wallet_status = 'ACTIVE';

CREATE UNIQUE INDEX IF NOT EXISTS ux_merchant_users__active_user_merchant
ON merchants.merchant_users (merchant_id, user_id)
WHERE merchant_user_status = 'ACTIVE';

-- identity.users, roles, permissions, user_roles, role_permissions, service_identities
ALTER TABLE identity.users ADD CONSTRAINT uq_users__username UNIQUE (username);
ALTER TABLE identity.users ADD CONSTRAINT uq_users__email_normalized UNIQUE (email_normalized);
ALTER TABLE identity.roles ADD CONSTRAINT uq_roles__role_code UNIQUE (role_code);
ALTER TABLE identity.permissions ADD CONSTRAINT uq_permissions__permission_code UNIQUE (permission_code);
ALTER TABLE identity.service_identities ADD CONSTRAINT uq_service_identities__service_identity_code UNIQUE (service_identity_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_user_roles__active_user_role
ON identity.user_roles (user_id, role_id)
WHERE assignment_status = 'ACTIVE';

CREATE UNIQUE INDEX IF NOT EXISTS ux_role_permissions__active_role_permission
ON identity.role_permissions (role_id, permission_id)
WHERE binding_status = 'ACTIVE';

-- audit.audit_events, audit_trail_entries, security_events, evidence_links
-- Audit and evidence tables are append-oriented proof records. No broad hard uniqueness is applied.

-- integration.vendor_systems, vendor_endpoints, adapter_mappings, integration_credential_references, integration_health_records
ALTER TABLE integration.vendor_systems ADD CONSTRAINT uq_vendor_systems__vendor_code_environment UNIQUE (vendor_code, environment_code);
ALTER TABLE integration.vendor_endpoints ADD CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code UNIQUE (vendor_system_id, endpoint_code);
ALTER TABLE integration.integration_credential_references ADD CONSTRAINT uq_integration_credential_references__vendor_credential_code UNIQUE (vendor_system_id, credential_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_adapter_mappings__vendor_object_active
ON integration.adapter_mappings (vendor_system_id, mapping_type, vendor_object_type, vendor_object_ref)
WHERE mapping_status = 'ACTIVE';

-- Integration health records are observational evidence. No broad hard uniqueness is applied.

-- config.system_parameters, feature_flags, rate_limit_policies, ttl_policies, controlled_code_sets
ALTER TABLE config.system_parameters ADD CONSTRAINT uq_system_parameters__parameter_code UNIQUE (parameter_code);
ALTER TABLE config.rate_limit_policies ADD CONSTRAINT uq_rate_limit_policies__policy_code UNIQUE (policy_code);
ALTER TABLE config.ttl_policies ADD CONSTRAINT uq_ttl_policies__policy_code UNIQUE (policy_code);
ALTER TABLE config.controlled_code_sets ADD CONSTRAINT uq_controlled_code_sets__set_value_domain UNIQUE (code_set_name, code_value, code_domain);

CREATE UNIQUE INDEX IF NOT EXISTS ux_feature_flags__scoped_flag
ON config.feature_flags (
    flag_code,
    environment_code,
    COALESCE(site_group_id, '00000000-0000-0000-0000-000000000000'::uuid),
    COALESCE(site_id, '00000000-0000-0000-0000-000000000000'::uuid),
    COALESCE(merchant_id, '00000000-0000-0000-0000-000000000000'::uuid),
    COALESCE(payment_rail_id, '00000000-0000-0000-0000-000000000000'::uuid),
    COALESCE(service_identity_id, '00000000-0000-0000-0000-000000000000'::uuid)
);

-- events.domain_events, outbox_events, event_publications, dead_letter_records, consumer_checkpoints
CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_events__domain_event
ON events.outbox_events (domain_event_id)
WHERE domain_event_id IS NOT NULL;

ALTER TABLE events.event_publications ADD CONSTRAINT uq_event_publications__outbox_attempt_number UNIQUE (outbox_event_id, publication_attempt_number);

CREATE UNIQUE INDEX IF NOT EXISTS ux_consumer_checkpoints__consumer_scope
ON events.consumer_checkpoints (
    consumer_name,
    COALESCE(consumer_group, ''),
    COALESCE(subscription_name, ''),
    COALESCE(event_type, ''),
    COALESCE(aggregate_type, '')
);

-- Dead-letter records are exception evidence and may recur. No broad hard uniqueness is applied.

-- INDEXES
-- Rationalized in v1.2 draft 1.2.
-- This section keeps purpose-driven non-unique indexes for high-value lookup, correlation tracing, status queues,
-- time-range review, replay protection, reconciliation, audit reconstruction, and retention workflows.
-- Broad maintenance-column indexes from draft 1.0 were removed pending observed query evidence.

CREATE INDEX IF NOT EXISTS ix_parking_sessions__site_group_id ON core.parking_sessions (site_group_id);
CREATE INDEX IF NOT EXISTS ix_parking_sessions__site_id ON core.parking_sessions (site_id);
CREATE INDEX IF NOT EXISTS ix_parking_sessions__vendor_system_id ON core.parking_sessions (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_parking_sessions__vendor_session_status ON core.parking_sessions (vendor_session_status);
CREATE INDEX IF NOT EXISTS ix_parking_sessions__correlation_id ON core.parking_sessions (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_tariff_snapshots__parking_session_id ON core.tariff_snapshots (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_tariff_snapshots__superseded_by_tariff_snapshot_id ON core.tariff_snapshots (superseded_by_tariff_snapshot_id);
CREATE INDEX IF NOT EXISTS ix_tariff_snapshots__vendor_system_id ON core.tariff_snapshots (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_tariff_snapshots__statutory_discount_validation_id ON core.tariff_snapshots (statutory_discount_validation_id);
CREATE INDEX IF NOT EXISTS ix_tariff_snapshots__correlation_id ON core.tariff_snapshots (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_payment_attempts__parking_session_id ON core.payment_attempts (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_payment_attempts__tariff_snapshot_id ON core.payment_attempts (tariff_snapshot_id);
CREATE INDEX IF NOT EXISTS ix_payment_attempts__payment_rail_id ON core.payment_attempts (payment_rail_id);
CREATE INDEX IF NOT EXISTS ix_payment_attempts__attempt_status ON core.payment_attempts (attempt_status);
CREATE INDEX IF NOT EXISTS ix_payment_attempts__correlation_id ON core.payment_attempts (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_payment_confirmations__payment_attempt_id ON core.payment_confirmations (payment_attempt_id);
CREATE INDEX IF NOT EXISTS ix_payment_confirmations__provider_outcome_id ON core.payment_confirmations (provider_outcome_id);
CREATE INDEX IF NOT EXISTS ix_payment_confirmations__payment_rail_id ON core.payment_confirmations (payment_rail_id);
CREATE INDEX IF NOT EXISTS ix_payment_confirmations__confirmation_status ON core.payment_confirmations (confirmation_status);
CREATE INDEX IF NOT EXISTS ix_payment_confirmations__correlation_id ON core.payment_confirmations (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_exit_authorizations__parking_session_id ON core.exit_authorizations (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_exit_authorizations__payment_attempt_id ON core.exit_authorizations (payment_attempt_id);
CREATE INDEX IF NOT EXISTS ix_exit_authorizations__payment_confirmation_id ON core.exit_authorizations (payment_confirmation_id);
CREATE INDEX IF NOT EXISTS ix_exit_authorizations__authorization_status ON core.exit_authorizations (authorization_status);
CREATE INDEX IF NOT EXISTS ix_exit_authorizations__correlation_id ON core.exit_authorizations (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_payment_rails__rail_status ON payments.payment_rails (rail_status);
CREATE INDEX IF NOT EXISTS ix_provider_sessions__payment_attempt_id ON payments.provider_sessions (payment_attempt_id);
CREATE INDEX IF NOT EXISTS ix_provider_sessions__payment_rail_id ON payments.provider_sessions (payment_rail_id);
CREATE INDEX IF NOT EXISTS ix_provider_sessions__session_status ON payments.provider_sessions (session_status);
CREATE INDEX IF NOT EXISTS ix_provider_sessions__expires_at ON payments.provider_sessions (expires_at);
CREATE INDEX IF NOT EXISTS ix_provider_sessions__correlation_id ON payments.provider_sessions (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_provider_callbacks__payment_rail_id ON payments.provider_callbacks (payment_rail_id);
CREATE INDEX IF NOT EXISTS ix_provider_callbacks__provider_session_id ON payments.provider_callbacks (provider_session_id);
CREATE INDEX IF NOT EXISTS ix_provider_callbacks__payment_attempt_id ON payments.provider_callbacks (payment_attempt_id);
CREATE INDEX IF NOT EXISTS ix_provider_callbacks__verification_status ON payments.provider_callbacks (verification_status);
CREATE INDEX IF NOT EXISTS ix_provider_callbacks__processing_status ON payments.provider_callbacks (processing_status);
CREATE INDEX IF NOT EXISTS ix_provider_callbacks__received_at ON payments.provider_callbacks (received_at);
CREATE INDEX IF NOT EXISTS ix_provider_callbacks__correlation_id ON payments.provider_callbacks (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_provider_status_queries__payment_attempt_id ON payments.provider_status_queries (payment_attempt_id);
CREATE INDEX IF NOT EXISTS ix_provider_status_queries__provider_session_id ON payments.provider_status_queries (provider_session_id);
CREATE INDEX IF NOT EXISTS ix_provider_status_queries__payment_rail_id ON payments.provider_status_queries (payment_rail_id);
CREATE INDEX IF NOT EXISTS ix_provider_status_queries__query_status ON payments.provider_status_queries (query_status);
CREATE INDEX IF NOT EXISTS ix_provider_status_queries__correlation_id ON payments.provider_status_queries (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_provider_outcomes__payment_attempt_id ON payments.provider_outcomes (payment_attempt_id);
CREATE INDEX IF NOT EXISTS ix_provider_outcomes__provider_session_id ON payments.provider_outcomes (provider_session_id);
CREATE INDEX IF NOT EXISTS ix_provider_outcomes__provider_callback_id ON payments.provider_outcomes (provider_callback_id);
CREATE INDEX IF NOT EXISTS ix_provider_outcomes__payment_rail_id ON payments.provider_outcomes (payment_rail_id);
CREATE INDEX IF NOT EXISTS ix_provider_outcomes__correlation_id ON payments.provider_outcomes (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_session_resolution_requests__site_group_id ON sessions.session_resolution_requests (site_group_id);
CREATE INDEX IF NOT EXISTS ix_session_resolution_requests__site_id ON sessions.session_resolution_requests (site_id);
CREATE INDEX IF NOT EXISTS ix_session_resolution_requests__request_status ON sessions.session_resolution_requests (request_status);
CREATE INDEX IF NOT EXISTS ix_session_resolution_requests__requested_at ON sessions.session_resolution_requests (requested_at);
CREATE INDEX IF NOT EXISTS ix_session_resolution_requests__correlation_id ON sessions.session_resolution_requests (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_session_resolution_results__session_resolution_request_id ON sessions.session_resolution_results (session_resolution_request_id);
CREATE INDEX IF NOT EXISTS ix_session_resolution_results__parking_session_id ON sessions.session_resolution_results (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_session_resolution_results__site_group_id ON sessions.session_resolution_results (site_group_id);
CREATE INDEX IF NOT EXISTS ix_session_resolution_results__site_id ON sessions.session_resolution_results (site_id);
CREATE INDEX IF NOT EXISTS ix_session_resolution_results__correlation_id ON sessions.session_resolution_results (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_session_lookup_cache__site_group_id ON sessions.session_lookup_cache (site_group_id);
CREATE INDEX IF NOT EXISTS ix_session_lookup_cache__site_id ON sessions.session_lookup_cache (site_id);
CREATE INDEX IF NOT EXISTS ix_session_lookup_cache__parking_session_id ON sessions.session_lookup_cache (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_session_lookup_cache__vendor_system_id ON sessions.session_lookup_cache (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_session_lookup_cache__correlation_id ON sessions.session_lookup_cache (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_session_identifier_indexes__parking_session_id ON sessions.session_identifier_indexes (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_session_identifier_indexes__site_group_id ON sessions.session_identifier_indexes (site_group_id);
CREATE INDEX IF NOT EXISTS ix_session_identifier_indexes__site_id ON sessions.session_identifier_indexes (site_id);
CREATE INDEX IF NOT EXISTS ix_session_identifier_indexes__vendor_system_id ON sessions.session_identifier_indexes (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_session_identifier_indexes__correlation_id ON sessions.session_identifier_indexes (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_coupons__merchant_id ON coupons.coupons (merchant_id);
CREATE INDEX IF NOT EXISTS ix_coupons__coupon_status ON coupons.coupons (coupon_status);
CREATE INDEX IF NOT EXISTS ix_coupon_rule_groups__coupon_id ON coupons.coupon_rule_groups (coupon_id);
CREATE INDEX IF NOT EXISTS ix_coupon_rule_groups__rule_group_status ON coupons.coupon_rule_groups (rule_group_status);
CREATE INDEX IF NOT EXISTS ix_coupon_rules__coupon_rule_group_id ON coupons.coupon_rules (coupon_rule_group_id);
CREATE INDEX IF NOT EXISTS ix_coupon_rules__site_group_id ON coupons.coupon_rules (site_group_id);
CREATE INDEX IF NOT EXISTS ix_coupon_rules__site_id ON coupons.coupon_rules (site_id);
CREATE INDEX IF NOT EXISTS ix_coupon_applications__coupon_id ON coupons.coupon_applications (coupon_id);
CREATE INDEX IF NOT EXISTS ix_coupon_applications__merchant_id ON coupons.coupon_applications (merchant_id);
CREATE INDEX IF NOT EXISTS ix_coupon_applications__merchant_wallet_id ON coupons.coupon_applications (merchant_wallet_id);
CREATE INDEX IF NOT EXISTS ix_coupon_applications__parking_session_id ON coupons.coupon_applications (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_coupon_applications__correlation_id ON coupons.coupon_applications (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_discount_policy_references__site_group_id ON discounts.discount_policy_references (site_group_id);
CREATE INDEX IF NOT EXISTS ix_discount_policy_references__site_id ON discounts.discount_policy_references (site_id);
CREATE INDEX IF NOT EXISTS ix_discount_policy_references__parent_policy_reference_id ON discounts.discount_policy_references (parent_policy_reference_id);
CREATE INDEX IF NOT EXISTS ix_statutory_discount_validations__parking_session_id ON discounts.statutory_discount_validations (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_statutory_discount_validations__tariff_snapshot_id ON discounts.statutory_discount_validations (tariff_snapshot_id);
CREATE INDEX IF NOT EXISTS ix_statutory_discount_validations__applied_policy_reference_ ON discounts.statutory_discount_validations (applied_policy_reference_id);
CREATE INDEX IF NOT EXISTS ix_statutory_discount_validations__fallback_policy_reference ON discounts.statutory_discount_validations (fallback_policy_reference_id);
CREATE INDEX IF NOT EXISTS ix_statutory_discount_validations__correlation_id ON discounts.statutory_discount_validations (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_discount_evidence_references__statutory_discount_validati ON discounts.discount_evidence_references (statutory_discount_validation_id);
CREATE INDEX IF NOT EXISTS ix_discount_evidence_references__evidence_capture_status ON discounts.discount_evidence_references (evidence_capture_status);
CREATE INDEX IF NOT EXISTS ix_discount_evidence_references__redaction_status ON discounts.discount_evidence_references (redaction_status);
CREATE INDEX IF NOT EXISTS ix_discount_evidence_references__retention_expires_at ON discounts.discount_evidence_references (retention_expires_at);
CREATE INDEX IF NOT EXISTS ix_discount_evidence_references__correlation_id ON discounts.discount_evidence_references (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_gate_devices__site_id ON gates.gate_devices (site_id);
CREATE INDEX IF NOT EXISTS ix_gate_devices__lane_id ON gates.gate_devices (lane_id);
CREATE INDEX IF NOT EXISTS ix_gate_devices__service_identity_id ON gates.gate_devices (service_identity_id);
CREATE INDEX IF NOT EXISTS ix_gate_authorization_consumptions__exit_authorization_id ON gates.gate_authorization_consumptions (exit_authorization_id);
CREATE INDEX IF NOT EXISTS ix_gate_authorization_consumptions__gate_device_id ON gates.gate_authorization_consumptions (gate_device_id);
CREATE INDEX IF NOT EXISTS ix_gate_authorization_consumptions__site_id ON gates.gate_authorization_consumptions (site_id);
CREATE INDEX IF NOT EXISTS ix_gate_authorization_consumptions__lane_id ON gates.gate_authorization_consumptions (lane_id);
CREATE INDEX IF NOT EXISTS ix_gate_authorization_consumptions__consume_status ON gates.gate_authorization_consumptions (consume_status);
CREATE INDEX IF NOT EXISTS ix_gate_authorization_consumptions__command_result_status ON gates.gate_authorization_consumptions (command_result_status);
CREATE INDEX IF NOT EXISTS ix_gate_authorization_consumptions__correlation_id ON gates.gate_authorization_consumptions (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_gate_events__gate_device_id ON gates.gate_events (gate_device_id);
CREATE INDEX IF NOT EXISTS ix_gate_events__exit_authorization_id ON gates.gate_events (exit_authorization_id);
CREATE INDEX IF NOT EXISTS ix_gate_events__site_id ON gates.gate_events (site_id);
CREATE INDEX IF NOT EXISTS ix_gate_events__lane_id ON gates.gate_events (lane_id);
CREATE INDEX IF NOT EXISTS ix_gate_events__event_status ON gates.gate_events (event_status);
CREATE INDEX IF NOT EXISTS ix_gate_events__occurred_at ON gates.gate_events (occurred_at);
CREATE INDEX IF NOT EXISTS ix_gate_events__correlation_id ON gates.gate_events (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_gate_heartbeats__gate_device_id ON gates.gate_heartbeats (gate_device_id);
CREATE INDEX IF NOT EXISTS ix_gate_heartbeats__site_id ON gates.gate_heartbeats (site_id);
CREATE INDEX IF NOT EXISTS ix_gate_heartbeats__lane_id ON gates.gate_heartbeats (lane_id);
CREATE INDEX IF NOT EXISTS ix_gate_heartbeats__heartbeat_status ON gates.gate_heartbeats (heartbeat_status);
CREATE INDEX IF NOT EXISTS ix_gate_heartbeats__device_reported_status ON gates.gate_heartbeats (device_reported_status);
CREATE INDEX IF NOT EXISTS ix_gate_heartbeats__observed_at ON gates.gate_heartbeats (observed_at);
CREATE INDEX IF NOT EXISTS ix_gate_heartbeats__correlation_id ON gates.gate_heartbeats (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_manual_gate_logs__parking_session_id ON operations.manual_gate_logs (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_manual_gate_logs__exit_authorization_id ON operations.manual_gate_logs (exit_authorization_id);
CREATE INDEX IF NOT EXISTS ix_manual_gate_logs__incident_record_id ON operations.manual_gate_logs (incident_record_id);
CREATE INDEX IF NOT EXISTS ix_manual_gate_logs__override_approval_id ON operations.manual_gate_logs (override_approval_id);
CREATE INDEX IF NOT EXISTS ix_manual_gate_logs__site_id ON operations.manual_gate_logs (site_id);
CREATE INDEX IF NOT EXISTS ix_manual_gate_logs__lane_id ON operations.manual_gate_logs (lane_id);
CREATE INDEX IF NOT EXISTS ix_manual_gate_logs__correlation_id ON operations.manual_gate_logs (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_override_requests__incident_record_id ON operations.override_requests (incident_record_id);
CREATE INDEX IF NOT EXISTS ix_override_requests__site_id ON operations.override_requests (site_id);
CREATE INDEX IF NOT EXISTS ix_override_requests__lane_id ON operations.override_requests (lane_id);
CREATE INDEX IF NOT EXISTS ix_override_requests__requested_by_user_id ON operations.override_requests (requested_by_user_id);
CREATE INDEX IF NOT EXISTS ix_override_requests__correlation_id ON operations.override_requests (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_override_approvals__override_request_id ON operations.override_approvals (override_request_id);
CREATE INDEX IF NOT EXISTS ix_override_approvals__decided_by_user_id ON operations.override_approvals (decided_by_user_id);
CREATE INDEX IF NOT EXISTS ix_override_approvals__correlation_id ON operations.override_approvals (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_incident_records__site_group_id ON operations.incident_records (site_group_id);
CREATE INDEX IF NOT EXISTS ix_incident_records__site_id ON operations.incident_records (site_id);
CREATE INDEX IF NOT EXISTS ix_incident_records__lane_id ON operations.incident_records (lane_id);
CREATE INDEX IF NOT EXISTS ix_incident_records__gate_device_id ON operations.incident_records (gate_device_id);
CREATE INDEX IF NOT EXISTS ix_incident_records__correlation_id ON operations.incident_records (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_operator_action_logs__operator_user_id ON operations.operator_action_logs (operator_user_id);
CREATE INDEX IF NOT EXISTS ix_operator_action_logs__site_id ON operations.operator_action_logs (site_id);
CREATE INDEX IF NOT EXISTS ix_operator_action_logs__correlation_id ON operations.operator_action_logs (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_mops_transaction_records__parking_session_id ON reconciliation.mops_transaction_records (parking_session_id);
CREATE INDEX IF NOT EXISTS ix_mops_transaction_records__manual_gate_log_id ON reconciliation.mops_transaction_records (manual_gate_log_id);
CREATE INDEX IF NOT EXISTS ix_mops_transaction_records__incident_record_id ON reconciliation.mops_transaction_records (incident_record_id);
CREATE INDEX IF NOT EXISTS ix_mops_transaction_records__site_id ON reconciliation.mops_transaction_records (site_id);
CREATE INDEX IF NOT EXISTS ix_mops_transaction_records__lane_id ON reconciliation.mops_transaction_records (lane_id);
CREATE INDEX IF NOT EXISTS ix_mops_transaction_records__imported_by_service_identity_id ON reconciliation.mops_transaction_records (imported_by_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_mops_transaction_records__correlation_id ON reconciliation.mops_transaction_records (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_reconciliation_runs__site_group_id ON reconciliation.reconciliation_runs (site_group_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_runs__site_id ON reconciliation.reconciliation_runs (site_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_runs__incident_record_id ON reconciliation.reconciliation_runs (incident_record_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_runs__payment_rail_id ON reconciliation.reconciliation_runs (payment_rail_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_runs__correlation_id ON reconciliation.reconciliation_runs (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_reconciliation_items__reconciliation_run_id ON reconciliation.reconciliation_items (reconciliation_run_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_items__mops_transaction_record_id ON reconciliation.reconciliation_items (mops_transaction_record_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_items__manual_gate_log_id ON reconciliation.reconciliation_items (manual_gate_log_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_items__payment_attempt_id ON reconciliation.reconciliation_items (payment_attempt_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_items__payment_confirmation_id ON reconciliation.reconciliation_items (payment_confirmation_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_items__provider_outcome_id ON reconciliation.reconciliation_items (provider_outcome_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_items__correlation_id ON reconciliation.reconciliation_items (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_reconciliation_exceptions__reconciliation_run_id ON reconciliation.reconciliation_exceptions (reconciliation_run_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_exceptions__reconciliation_item_id ON reconciliation.reconciliation_exceptions (reconciliation_item_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_exceptions__incident_record_id ON reconciliation.reconciliation_exceptions (incident_record_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_exceptions__assigned_to_user_id ON reconciliation.reconciliation_exceptions (assigned_to_user_id);
CREATE INDEX IF NOT EXISTS ix_reconciliation_exceptions__correlation_id ON reconciliation.reconciliation_exceptions (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_settlement_comparison_records__reconciliation_item_id ON reconciliation.settlement_comparison_records (reconciliation_item_id);
CREATE INDEX IF NOT EXISTS ix_settlement_comparison_records__mops_transaction_record_id ON reconciliation.settlement_comparison_records (mops_transaction_record_id);
CREATE INDEX IF NOT EXISTS ix_settlement_comparison_records__reconciliation_exception_i ON reconciliation.settlement_comparison_records (reconciliation_exception_id);
CREATE INDEX IF NOT EXISTS ix_settlement_comparison_records__payment_confirmation_id ON reconciliation.settlement_comparison_records (payment_confirmation_id);
CREATE INDEX IF NOT EXISTS ix_settlement_comparison_records__correlation_id ON reconciliation.settlement_comparison_records (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_site_groups__site_group_status ON sites.site_groups (site_group_status);
CREATE INDEX IF NOT EXISTS ix_sites__site_group_id ON sites.sites (site_group_id);
CREATE INDEX IF NOT EXISTS ix_sites__site_status ON sites.sites (site_status);
CREATE INDEX IF NOT EXISTS ix_lanes__site_id ON sites.lanes (site_id);
CREATE INDEX IF NOT EXISTS ix_lanes__lane_status ON sites.lanes (lane_status);
CREATE INDEX IF NOT EXISTS ix_device_assignments__site_id ON sites.device_assignments (site_id);
CREATE INDEX IF NOT EXISTS ix_device_assignments__lane_id ON sites.device_assignments (lane_id);
CREATE INDEX IF NOT EXISTS ix_device_assignments__gate_device_id ON sites.device_assignments (gate_device_id);
CREATE INDEX IF NOT EXISTS ix_merchants__merchant_status ON merchants.merchants (merchant_status);
CREATE INDEX IF NOT EXISTS ix_merchant_site_scopes__merchant_id ON merchants.merchant_site_scopes (merchant_id);
CREATE INDEX IF NOT EXISTS ix_merchant_site_scopes__site_group_id ON merchants.merchant_site_scopes (site_group_id);
CREATE INDEX IF NOT EXISTS ix_merchant_site_scopes__site_id ON merchants.merchant_site_scopes (site_id);
CREATE INDEX IF NOT EXISTS ix_merchant_wallets__merchant_id ON merchants.merchant_wallets (merchant_id);
CREATE INDEX IF NOT EXISTS ix_merchant_wallets__wallet_status ON merchants.merchant_wallets (wallet_status);
CREATE INDEX IF NOT EXISTS ix_merchant_users__merchant_id ON merchants.merchant_users (merchant_id);
CREATE INDEX IF NOT EXISTS ix_merchant_users__user_id ON merchants.merchant_users (user_id);
CREATE INDEX IF NOT EXISTS ix_merchant_users__merchant_user_status ON merchants.merchant_users (merchant_user_status);
CREATE INDEX IF NOT EXISTS ix_users__user_status ON identity.users (user_status);
CREATE INDEX IF NOT EXISTS ix_users__last_login_at ON identity.users (last_login_at);
CREATE INDEX IF NOT EXISTS ix_users__locked_at ON identity.users (locked_at);
CREATE INDEX IF NOT EXISTS ix_roles__role_status ON identity.roles (role_status);
CREATE INDEX IF NOT EXISTS ix_permissions__permission_status ON identity.permissions (permission_status);
CREATE INDEX IF NOT EXISTS ix_user_roles__user_id ON identity.user_roles (user_id);
CREATE INDEX IF NOT EXISTS ix_user_roles__role_id ON identity.user_roles (role_id);
CREATE INDEX IF NOT EXISTS ix_user_roles__assigned_by_user_id ON identity.user_roles (assigned_by_user_id);
CREATE INDEX IF NOT EXISTS ix_role_permissions__role_id ON identity.role_permissions (role_id);
CREATE INDEX IF NOT EXISTS ix_role_permissions__permission_id ON identity.role_permissions (permission_id);
CREATE INDEX IF NOT EXISTS ix_role_permissions__assigned_by_user_id ON identity.role_permissions (assigned_by_user_id);
CREATE INDEX IF NOT EXISTS ix_service_identities__identity_status ON identity.service_identities (identity_status);
CREATE INDEX IF NOT EXISTS ix_service_identities__credential_expires_at ON identity.service_identities (credential_expires_at);
CREATE INDEX IF NOT EXISTS ix_service_identities__last_rotated_at ON identity.service_identities (last_rotated_at);
CREATE INDEX IF NOT EXISTS ix_audit_events__actor_user_id ON audit.audit_events (actor_user_id);
CREATE INDEX IF NOT EXISTS ix_audit_events__actor_service_identity_id ON audit.audit_events (actor_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_audit_events__created_by_service_identity_id ON audit.audit_events (created_by_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_audit_events__occurred_at ON audit.audit_events (occurred_at);
CREATE INDEX IF NOT EXISTS ix_audit_events__recorded_at ON audit.audit_events (recorded_at);
CREATE INDEX IF NOT EXISTS ix_audit_events__correlation_id ON audit.audit_events (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_audit_events__causation_id ON audit.audit_events (causation_id) WHERE causation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_audit_trail_entries__audit_event_id ON audit.audit_trail_entries (audit_event_id);
CREATE INDEX IF NOT EXISTS ix_audit_trail_entries__changed_by_user_id ON audit.audit_trail_entries (changed_by_user_id);
CREATE INDEX IF NOT EXISTS ix_audit_trail_entries__changed_by_service_identity_id ON audit.audit_trail_entries (changed_by_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_audit_trail_entries__created_by_service_identity_id ON audit.audit_trail_entries (created_by_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_audit_trail_entries__changed_at ON audit.audit_trail_entries (changed_at);
CREATE INDEX IF NOT EXISTS ix_audit_trail_entries__correlation_id ON audit.audit_trail_entries (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_security_events__audit_event_id ON audit.security_events (audit_event_id);
CREATE INDEX IF NOT EXISTS ix_security_events__actor_user_id ON audit.security_events (actor_user_id);
CREATE INDEX IF NOT EXISTS ix_security_events__actor_service_identity_id ON audit.security_events (actor_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_security_events__incident_record_id ON audit.security_events (incident_record_id);
CREATE INDEX IF NOT EXISTS ix_security_events__resolved_by_user_id ON audit.security_events (resolved_by_user_id);
CREATE INDEX IF NOT EXISTS ix_security_events__created_by_service_identity_id ON audit.security_events (created_by_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_security_events__correlation_id ON audit.security_events (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_evidence_links__audit_event_id ON audit.evidence_links (audit_event_id);
CREATE INDEX IF NOT EXISTS ix_evidence_links__security_event_id ON audit.evidence_links (security_event_id);
CREATE INDEX IF NOT EXISTS ix_evidence_links__linked_by_user_id ON audit.evidence_links (linked_by_user_id);
CREATE INDEX IF NOT EXISTS ix_evidence_links__linked_by_service_identity_id ON audit.evidence_links (linked_by_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_evidence_links__purged_by_user_id ON audit.evidence_links (purged_by_user_id);
CREATE INDEX IF NOT EXISTS ix_evidence_links__retention_expires_at ON audit.evidence_links (retention_expires_at);
CREATE INDEX IF NOT EXISTS ix_evidence_links__correlation_id ON audit.evidence_links (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_vendor_systems__vendor_system_status ON integration.vendor_systems (vendor_system_status);
CREATE INDEX IF NOT EXISTS ix_vendor_endpoints__vendor_system_id ON integration.vendor_endpoints (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_vendor_endpoints__credential_reference_id ON integration.vendor_endpoints (credential_reference_id);
CREATE INDEX IF NOT EXISTS ix_vendor_endpoints__endpoint_status ON integration.vendor_endpoints (endpoint_status);
CREATE INDEX IF NOT EXISTS ix_adapter_mappings__vendor_system_id ON integration.adapter_mappings (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_adapter_mappings__site_group_id ON integration.adapter_mappings (site_group_id);
CREATE INDEX IF NOT EXISTS ix_adapter_mappings__site_id ON integration.adapter_mappings (site_id);
CREATE INDEX IF NOT EXISTS ix_adapter_mappings__lane_id ON integration.adapter_mappings (lane_id);
CREATE INDEX IF NOT EXISTS ix_adapter_mappings__gate_device_id ON integration.adapter_mappings (gate_device_id);
CREATE INDEX IF NOT EXISTS ix_integration_credential_references__vendor_system_id ON integration.integration_credential_references (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_integration_credential_references__service_identity_id ON integration.integration_credential_references (service_identity_id);
CREATE INDEX IF NOT EXISTS ix_integration_credential_references__credential_status ON integration.integration_credential_references (credential_status);
CREATE INDEX IF NOT EXISTS ix_integration_credential_references__last_rotated_at ON integration.integration_credential_references (last_rotated_at);
CREATE INDEX IF NOT EXISTS ix_integration_credential_references__next_rotation_due_at ON integration.integration_credential_references (next_rotation_due_at);
CREATE INDEX IF NOT EXISTS ix_integration_health_records__vendor_system_id ON integration.integration_health_records (vendor_system_id);
CREATE INDEX IF NOT EXISTS ix_integration_health_records__vendor_endpoint_id ON integration.integration_health_records (vendor_endpoint_id);
CREATE INDEX IF NOT EXISTS ix_integration_health_records__site_group_id ON integration.integration_health_records (site_group_id);
CREATE INDEX IF NOT EXISTS ix_integration_health_records__site_id ON integration.integration_health_records (site_id);
CREATE INDEX IF NOT EXISTS ix_integration_health_records__correlation_id ON integration.integration_health_records (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_system_parameters__parameter_status ON config.system_parameters (parameter_status);
CREATE INDEX IF NOT EXISTS ix_system_parameters__approved_at ON config.system_parameters (approved_at);
CREATE INDEX IF NOT EXISTS ix_feature_flags__site_group_id ON config.feature_flags (site_group_id);
CREATE INDEX IF NOT EXISTS ix_feature_flags__site_id ON config.feature_flags (site_id);
CREATE INDEX IF NOT EXISTS ix_feature_flags__merchant_id ON config.feature_flags (merchant_id);
CREATE INDEX IF NOT EXISTS ix_rate_limit_policies__policy_status ON config.rate_limit_policies (policy_status);
CREATE INDEX IF NOT EXISTS ix_ttl_policies__policy_status ON config.ttl_policies (policy_status);
CREATE INDEX IF NOT EXISTS ix_controlled_code_sets__code_status ON config.controlled_code_sets (code_status);
CREATE INDEX IF NOT EXISTS ix_domain_events__actor_user_id ON events.domain_events (actor_user_id);
CREATE INDEX IF NOT EXISTS ix_domain_events__actor_service_identity_id ON events.domain_events (actor_service_identity_id);
CREATE INDEX IF NOT EXISTS ix_domain_events__event_status ON events.domain_events (event_status);
CREATE INDEX IF NOT EXISTS ix_domain_events__correlation_id ON events.domain_events (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_domain_events__causation_id ON events.domain_events (causation_id) WHERE causation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_outbox_events__domain_event_id ON events.outbox_events (domain_event_id);
CREATE INDEX IF NOT EXISTS ix_outbox_events__publication_status ON events.outbox_events (publication_status);
CREATE INDEX IF NOT EXISTS ix_outbox_events__available_at ON events.outbox_events (available_at);
CREATE INDEX IF NOT EXISTS ix_outbox_events__published_at ON events.outbox_events (published_at);
CREATE INDEX IF NOT EXISTS ix_outbox_events__next_retry_at ON events.outbox_events (next_retry_at);
CREATE INDEX IF NOT EXISTS ix_outbox_events__correlation_id ON events.outbox_events (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_outbox_events__causation_id ON events.outbox_events (causation_id) WHERE causation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_event_publications__outbox_event_id ON events.event_publications (outbox_event_id);
CREATE INDEX IF NOT EXISTS ix_event_publications__publication_status ON events.event_publications (publication_status);
CREATE INDEX IF NOT EXISTS ix_event_publications__started_at ON events.event_publications (started_at);
CREATE INDEX IF NOT EXISTS ix_event_publications__completed_at ON events.event_publications (completed_at);
CREATE INDEX IF NOT EXISTS ix_event_publications__correlation_id ON events.event_publications (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_dead_letter_records__outbox_event_id ON events.dead_letter_records (outbox_event_id);
CREATE INDEX IF NOT EXISTS ix_dead_letter_records__event_publication_id ON events.dead_letter_records (event_publication_id);
CREATE INDEX IF NOT EXISTS ix_dead_letter_records__dead_letter_status ON events.dead_letter_records (dead_letter_status);
CREATE INDEX IF NOT EXISTS ix_dead_letter_records__dead_lettered_at ON events.dead_letter_records (dead_lettered_at);
CREATE INDEX IF NOT EXISTS ix_dead_letter_records__resolved_at ON events.dead_letter_records (resolved_at);
CREATE INDEX IF NOT EXISTS ix_dead_letter_records__replay_requested_at ON events.dead_letter_records (replay_requested_at);
CREATE INDEX IF NOT EXISTS ix_dead_letter_records__correlation_id ON events.dead_letter_records (correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_consumer_checkpoints__last_outbox_event_id ON events.consumer_checkpoints (last_outbox_event_id);
CREATE INDEX IF NOT EXISTS ix_consumer_checkpoints__last_domain_event_id ON events.consumer_checkpoints (last_domain_event_id);
CREATE INDEX IF NOT EXISTS ix_consumer_checkpoints__checkpoint_status ON events.consumer_checkpoints (checkpoint_status);
CREATE INDEX IF NOT EXISTS ix_consumer_checkpoints__last_processed_at ON events.consumer_checkpoints (last_processed_at);
CREATE INDEX IF NOT EXISTS ix_consumer_checkpoints__correlation_id ON events.consumer_checkpoints (correlation_id) WHERE correlation_id IS NOT NULL;

-- CONTROLLED CODE SET SEED BASELINE
-- ============================================================
-- The full controlled-code catalog should be generated from governance-approved seed files.
-- This draft includes only the table structure and a minimal placeholder comment to avoid inventing operational codes.

-- ============================================================
-- ROUTINE PLACEHOLDERS FOR INVARIANT-BEARING TRANSITIONS
-- ============================================================
-- Full routine bodies must be implemented and tested in the Engineering Pack / migration baseline.
CREATE OR REPLACE FUNCTION core.create_or_reuse_payment_attempt() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'core.create_or_reuse_payment_attempt is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION core.record_payment_confirmation() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'core.record_payment_confirmation is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION core.issue_exit_authorization() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'core.issue_exit_authorization is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION gates.consume_exit_authorization() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'gates.consume_exit_authorization is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION coupons.reserve_coupon_application() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'coupons.reserve_coupon_application is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION coupons.commit_coupon_application() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'coupons.commit_coupon_application is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION coupons.release_coupon_application() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'coupons.release_coupon_application is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION discounts.record_statutory_discount_validation() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'discounts.record_statutory_discount_validation is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION operations.record_manual_gate_log() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'operations.record_manual_gate_log is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION reconciliation.import_mops_transaction_record() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'reconciliation.import_mops_transaction_record is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION reconciliation.resolve_reconciliation_item() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'reconciliation.resolve_reconciliation_item is a v1.2 routine placeholder and must be implemented before production use'; END; $$;
CREATE OR REPLACE FUNCTION events.enqueue_outbox_event() RETURNS void LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'events.enqueue_outbox_event is a v1.2 routine placeholder and must be implemented before production use'; END; $$;

COMMIT;
