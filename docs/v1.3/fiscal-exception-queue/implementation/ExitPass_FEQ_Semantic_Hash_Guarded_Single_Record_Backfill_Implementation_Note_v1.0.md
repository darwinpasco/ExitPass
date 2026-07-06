# ExitPass FEQ Semantic Hash Guarded Single-Record Backfill Implementation Note v1.0

## Scope

This slice implements the guarded, single-record semantic hash metadata mutation path for Central PMS FEQ legacy semantic hashes. It updates only one fiscal issuance reference at a time and only after the existing recalculation preview, controlled backfill approval, mutation-preparation audit, actor authorization, explicit approval, and dual-control gates pass.

This slice does not add automatic batch backfill, retry execution, retry workers, executable retry jobs, retry endpoints, POS Server POST calls, fiscal-gated ExitAuthorization enforcement, fiscal number editing, or manual fiscal document creation.

## Default Configuration

Controlled semantic hash mutation remains disabled by default through `FiscalExceptionSemanticHashControlledBackfillMutationOptions.EnableControlledMutation`.

When disabled, the guarded mutation service returns a disabled posture and does not call the mutation repository. The previously prepared dry-run posture remains available for audit and review without mutating `core.fiscal_issuance_references`.

## Guarded Mutation Behavior

The guarded mutation service accepts a single fiscal issuance reference basis and requires:

- latest approval posture is `ReadyForControlledBackfill`;
- latest recalculation preview audit exists and matches the approval basis;
- latest mutation-preparation audit exists and is `PreparedForControlledMutation`;
- actor/service authorization is present;
- explicit approval is present;
- dual-control is satisfied when required;
- mutation mode is `SingleRecordOnly`;
- recalculated semantic hash metadata is complete and current `sha256:v1`;
- the fiscal issuance reference still has legacy source version `central-pms-pos-server-fiscal-request-v1`;
- retry execution remains unavailable.

Basis mismatches fail closed with stale/blocked postures and do not update the reference.

## Transaction And Safety Rules

The PostgreSQL mutation repository performs the guarded write in one database transaction. Inside that transaction it:

- re-reads and locks the fiscal issuance reference row;
- verifies the stored semantic hash source version is still the expected legacy version;
- verifies the approved recalculation preview audit still matches the command basis;
- verifies the mutation-preparation audit still matches the command basis;
- verifies recalculated hash value, algorithm, source version, fact count, and safe summary;
- writes a mutation audit row;
- updates only semantic hash metadata fields on `core.fiscal_issuance_references`;
- fails closed if the row or approved basis has changed.

No fiscal document evidence, fiscal number, payment finality, ExitAuthorization, gate state, or retry execution state is updated.

## Audit Behavior

The existing controlled backfill mutation audit table is extended with `mutation_preparation_audit_id` so successful guarded writes can link back to the approved mutation-preparation audit. Mutation audit records capture attempted/succeeded/stale/blocked posture, fiscal issuance reference id, preview audit id, mutation-preparation audit id, approval and dual-control references, old/new semantic hash metadata, actor/service identity, correlation id, and safe summary.

The audit surface stores safe metadata only. It does not store raw POS Server payloads, raw payment provider payloads, secrets, customer PII, raw statutory evidence, or full canonical source text.

## Fields Mutated

Successful guarded mutation updates only these `core.fiscal_issuance_references` columns:

- `semantic_request_hash_status`
- `semantic_request_hash_value`
- `semantic_request_hash_algorithm`
- `semantic_request_hash_source_version`
- `semantic_request_hash_source_fact_count`
- `semantic_request_hash_safe_summary`
- `semantic_request_hash_recorded_at`

## FEQ Detail And Readiness Impact

FEQ read-only detail can now surface the latest guarded mutation audit posture, including old/new semantic hash source versions, new hash value, block reason, mutation status, and safe summary. After a successful guarded mutation, semantic hash readiness can evaluate the fiscal issuance reference as current-ready when the persisted metadata is complete `sha256:v1`.

`RetryExecutionAvailable` remains false.

## Validation

Tests cover disabled default behavior, missing approval/precondition gates, actor/approval/dual-control blocks, stale preview and mutation-preparation basis mismatches, dry-run/no-mutation behavior, successful guarded single-record mutation, transaction-coupled audit, semantic hash readiness after mutation, and preservation of payment, fiscal document, ExitAuthorization, gate, fiscal number, and retry execution boundaries.

## Remaining Blockers Before Retry Execution

Retry execution remains blocked until a separate slice explicitly implements and validates controlled retry execution. This slice only makes legacy semantic hash metadata eligible for current-hash readiness after a guarded, approved, single-record mutation.
