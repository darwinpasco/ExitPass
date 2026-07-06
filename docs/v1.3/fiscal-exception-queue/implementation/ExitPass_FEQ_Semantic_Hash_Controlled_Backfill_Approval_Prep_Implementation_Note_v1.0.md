# ExitPass FEQ Semantic Hash Controlled Backfill Approval Prep Implementation Note v1.0

## Scope

This slice adds a non-mutating approval/precondition posture for future controlled semantic hash backfill.

It does not update historical `fiscal_issuance_references` semantic hash metadata, perform backfill, make legacy records retry-safe, execute retry, schedule executable retry work, expose a retry endpoint, or call POS Server POST.

## Approval/precondition model

`FiscalExceptionSemanticHashControlledBackfillApprovalService` evaluates FEQ detail plus the latest semantic hash recalculation preview audit summary.

The result exposes:

- controlled backfill approval status
- safe block reason and summary
- legacy and required source versions
- latest recalculation preview audit basis
- preview success and complete original fact posture
- recalculated `sha256:v1` metadata posture
- approval policy, explicit approval, actor/service authorization, and dual-control posture
- mutation status, always non-mutating in this slice
- retry/POS Server/payment/ExitAuthorization/gate/manual fiscal document side-effect flags, all false

## Required gates before mutation

Future mutation may only be considered when all modeled gates pass:

- the record is legacy `central-pms-pos-server-fiscal-request-v1`
- the latest recalculation preview audit exists
- the preview was successful and based on complete original request facts
- the preview produced a complete current `sha256:v1` hash result
- preview mutation status is `NotMutated`
- the FEQ case is not in mismatch, manual-review, fiscal-conflict, closed, or reconciled posture
- approval policy is configured
- actor/service authorization is modeled as present
- explicit approval is present
- dual-control is satisfied when required
- retry execution remains disabled

## Dual-control posture

Controlled backfill approval is disabled by default because the configured approval policy, actor/service authorization, explicit approval, and dual-control satisfaction default to false or pending.

When all other gates pass but dual-control is required and not satisfied, the result is `PendingDualControl` with reason `semantic_hash_backfill_dual_control_required`.

## Mutation status

Mutation is deferred. The service returns a read-only approval posture only and always reports `FiscalIssuanceReferenceMutated = false`.

The existing recalculation preview audit summary was extended to expose safe latest-preview fields already stored in `core.fiscal_issuance_semantic_hash_recalculation_previews`; no new table or backfill write path was added.

## FEQ detail/readiness impact

FEQ detail now includes controlled backfill approval posture fields:

- approval status and block reason
- latest preview audit id and attempted timestamp
- dual-control, approval, and actor authorization posture
- mutation status
- safe summary

Legacy hashes remain blocked by semantic hash readiness. Retry eligibility, command preparation, scheduling preparation, and execution preparation remain blocked until a future approved mutation/backfill slice safely updates persisted hash metadata.

## Tests

Unit tests cover missing/blocked/incomplete preview audit, missing and non-`sha256:v1` recalculated hash metadata, missing approval policy, missing actor authorization, missing explicit approval, pending dual-control, ready-for-controlled-backfill posture, already-current records, incompatible source versions, FEQ detail projection, and no retry/mutation side effects.

The recalculation preview audit repository integration test now verifies that the latest safe preview basis is returned in the audit summary.

## Remaining blockers before retry execution

- No controlled semantic hash backfill mutation exists.
- Legacy `central-pms-pos-server-fiscal-request-v1` hashes remain not retry-safe.
- Retry execution remains unavailable.
- A future controlled backfill slice must implement audited, approved mutation separately before any execution slice can rely on historical legacy records.
