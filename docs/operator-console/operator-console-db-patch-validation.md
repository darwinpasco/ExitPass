# Operator Console Database Patch Validation

Status: local non-production validation passed; ready for controlled review and promotion.

## Patch

`infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`

Purpose: create the Operator Console database support slice for HR/Timekeeping identity mapping, imported operator shifts, shift revocation, controlled shift takeover, Operator Console device/browser binding, access evaluation evidence, and statutory entitlement fingerprint storage.

## Validation Environment

Validation was performed against local non-production PostgreSQL only:

- Database: `exitpass_v12_dev`
- Scope: local schema inspection, local patch execution, and local post-validation catalog checks
- Production or remote database execution: not performed

## Validation Result Summary

The patch executed successfully after the constraint-name cleanup.

- Migration execution succeeded.
- No SQL errors occurred.
- No PostgreSQL identifier truncation notices occurred.
- `operator_console` schema objects were created as expected.
- `discounts.statutory_entitlement_fingerprints` was created.
- Expected indexes were present: 54 total.
- Expected foreign keys were present: 76 total, including shortened FK names.
- Controlled-code seed rows were present for `OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE`: 5 rows.

## Objects Created

The patch creates:

- `operator_console` schema.
- Operator Console enum types for HR identity mapping status, shift operational status, shift revocation status, shift takeover status, device binding status, device trust level, and access evaluation status.
- `discounts.entitlement_fingerprint_status_enum`.
- Operator Console tables for HR identity mappings, operator shifts, shift versions, shift revocations, shift takeovers, device bindings, device assignment history, access evaluations, and access evaluation reasons.
- `discounts.statutory_entitlement_fingerprints`.
- Supporting primary keys, foreign keys, check constraints, unique indexes, and lookup indexes.
- Initial `config.controlled_code_sets` rows for `OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE`.

## Boundary Confirmations

The non-payment boundary remains explicit.

- No payment mutation DDL was added.
- No FK relationship touches payment, gate, coupon, provider, settlement, or WebPay tables.
- No gate, coupon, provider, settlement, or WebPay table mutation was added.
- Boundary scan found no AUB references.
- Baseline DDL was not modified.

Operator Console remains non-payment. It must not create, mutate, route, configure, or invoke payment attempts, payment confirmations, payment provider outcomes, exit authorizations, gate authorization consumptions, coupon applications, settlement truth records, provider routing, or payment finality.

## Promotion Note

This patch is locally validated and ready for controlled review/promotion.

Production application requires separate approval, release planning, backup/rollback planning, and an operational rollout window. Local validation does not authorize direct production execution.
