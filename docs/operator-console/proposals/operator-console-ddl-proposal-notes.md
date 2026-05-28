# Operator Console DDL Proposal Notes

Status: review-only notes for `operator-console-ddl-proposal.sql`.

The SQL proposal is not an executable migration. It was drafted from:

- ExitPass Full Database Creation DDL v1.2
- `docs/operator-console/operator-console-schema-extension-design.md`
- `docs/operator-console/statutory-validation-and-access-contract.md`

## Scope

The proposal adds a future `operator_console` schema for:

- HR/Timekeeping identity mapping
- imported operator shifts and immutable shift import versions
- shift revocation
- controlled shift takeover
- operator browser/device binding
- operator browser/device site assignment history
- operator access evaluation evidence

It also proposes `discounts.statutory_entitlement_fingerprints` for backend-generated entitlement duplicate-detection fingerprints.

## Review Notes

- `operator_console.hr_identity_mappings` uses `identity.users(user_id)` as the ExitPass user anchor and stores only hashed/masked HR identifiers.
- `operator_console.operator_shifts` stores current operational shift state, while `operator_console.operator_shift_versions` stores immutable import/version history.
- HR/Timekeeping status is modeled in two layers: ExitPass-controlled `import_status_code` plus raw source/provider fields (`source_system_code`, `source_status_code`, and `source_status_description`). Provider-specific HR statuses are not proposed as PostgreSQL enums.
- `operator_console.shift_revocations` and `operator_console.shift_takeovers` are auditable workflow tables with required reason codes and actor references.
- `operator_console.operator_device_bindings` is separate from `gates.gate_devices`; it models Operator Console browser/device trust and optional managed-device mTLS identity.
- `operator_console.operator_device_assignment_history` captures reconstructable device/site assignment history because site assignment affects authorization.
- `operator_console.operator_access_evaluations` persists only denied and controlled-action evaluations for MVP, with correlation, user, device, shift, site, and target fields. It does not persist every page load, tab switch, harmless read, or navigation event.
- `operator_console.operator_access_evaluation_reasons` normalizes access evaluation reason rows with controlled reason codes, message/source context, and indexes for audit/reporting.
- `discounts.statutory_entitlement_fingerprints` stores fingerprint hashes and salt/key references only, not raw statutory ID data or secret material.
- `duplicate_detection_scope` remains a controlled-code/reference-data value, not a hard PostgreSQL enum. The initial recommended code family is `OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE`, with initial values `SAME_SESSION_ONLY`, `SAME_SITE_ACTIVE_DAY`, `SAME_SITE_GROUP_ACTIVE_DAY`, `GLOBAL_ACTIVE_DAY`, and `CONFIGURED_POLICY_WINDOW`.

## Unresolved Review Questions

- Evidence storage ownership remains open: Audit/Event, Central PMS, a dedicated Evidence service, or another controlled service boundary.

## Non-Payment Boundary

The proposal does not create, mutate, or extend:

- `core.payment_attempts`
- `core.payment_confirmations`
- `payments.provider_outcomes`
- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- `coupons.coupon_applications`
- settlement truth records

Operator Console remains non-payment and may only display payment status as read-only context.

