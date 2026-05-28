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
- operator access evaluation evidence

It also proposes `discounts.statutory_entitlement_fingerprints` for backend-generated entitlement duplicate-detection fingerprints.

## Review Notes

- `operator_console.hr_identity_mappings` uses `identity.users(user_id)` as the ExitPass user anchor and stores only hashed/masked HR identifiers.
- `operator_console.operator_shifts` stores current operational shift state, while `operator_console.operator_shift_versions` stores immutable import/version history.
- `operator_console.shift_revocations` and `operator_console.shift_takeovers` are auditable workflow tables with required reason codes and actor references.
- `operator_console.operator_device_bindings` is separate from `gates.gate_devices`; it models Operator Console browser/device trust and optional managed-device mTLS identity.
- `operator_console.operator_access_evaluations` persists denied and controlled-action evaluations with correlation, user, device, shift, site, and target fields.
- `discounts.statutory_entitlement_fingerprints` stores fingerprint hashes and salt/key references only, not raw statutory ID data or secret material.

## Unresolved Review Questions

- Should access denial reasons remain as `text[]`, or should the approved migration use a child table for normalization and indexed reason-code search?
- Should HR/Timekeeping source status be a provider-normalized text field, a controlled code set, or provider-specific enums?
- Should Operator Console device/site assignment history be captured in a separate assignment history table in the first migration?
- Should entitlement fingerprint duplicate detection scope be an enum, controlled code table, or varchar controlled by reference data?
- Should `operator_access_evaluations` persist every read-only evaluation or only denied and controlled-action evaluations?

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

