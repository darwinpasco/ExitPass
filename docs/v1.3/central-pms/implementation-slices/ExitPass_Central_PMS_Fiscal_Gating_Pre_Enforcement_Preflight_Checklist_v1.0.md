# ExitPass Central PMS Fiscal Gating Pre-Enforcement Preflight Checklist v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS Fiscal Gating Pre-Enforcement Preflight Checklist |
| Version | v1.0 |
| Scope | Pre-enforcement UAT and rollout readiness checklist |
| Status | Implementation-slice checklist |

## Purpose

This checklist defines the minimum evidence required before any future branch enables fiscal-before-ExitAuthorization blocking.

This checklist does not enable enforcement, change ExitAuthorization behavior, call POS Server, create retry/readback workers, add Operator Console queues, or add Management Dashboard projections.

## Preflight Gates

- Enforcement remains default-off.
- Shadow evaluation remains available.
- `EnforcementWiredForBlocking` remains `false`.
- Current ExitAuthorization behavior remains unchanged for valid payment-finality cases.
- Missing fiscal reference context does not block current ExitAuthorization.
- Blocked fiscal states do not block current ExitAuthorization.
- Ready fiscal states do not change current ExitAuthorization outcome.
- Shadow decision evidence is emitted for `allow`, `block`, `not_required_by_policy`, `exception_release_only`, `manual_review_required`, and `not_evaluable`.
- Shadow payload includes safe payment/fiscal context where available.
- Shadow payload excludes raw provider callbacks, PAN/CVV, tokens, secrets, credentials, raw entitlement evidence, and unmanaged sensitive evidence images.
- POS Server live calls are not introduced.
- Retry scheduler and GET readback worker are not introduced.
- Operator Console and Management Dashboard behavior is unchanged.
- Manual release / exception release remains separate from normal ExitAuthorization.

## Automated Evidence

Expected pre-enforcement validation:

- `FiscalIssuance` unit tests pass.
- `ExitAuthorization` unit tests pass.
- `PaymentToExitOperationalEvidenceTests` pass.
- `FiscalIssuance` integration tests pass.
- Central PMS API project builds.
- `git diff --check` passes.

## Future Enforcement Branch Entry Criteria

A future blocking branch should not start until this preflight package shows:

- default-off options verified
- readiness-only mode verified
- enforcement decision contract verified
- shadow structured evidence verified
- existing payment-to-exit behavior verified unchanged
- no live POS Server dependency added to ExitAuthorization
- rollout and manual exception procedures approved outside this slice
