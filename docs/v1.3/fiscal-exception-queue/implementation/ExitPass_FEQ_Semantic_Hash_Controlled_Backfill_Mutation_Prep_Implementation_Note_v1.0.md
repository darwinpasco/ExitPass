# ExitPass FEQ Semantic Hash Controlled Backfill Mutation Prep Implementation Note v1.0

## Scope

This slice prepares the Central PMS controlled semantic hash backfill mutation path for a future single-record, manually approved update of legacy semantic hash metadata. It does not run automatic backfill, execute retry, call POS Server, create retry jobs, expose retry endpoints, or change payment finality, ExitAuthorization, gate behavior, fiscal numbering, or manual fiscal document handling.

## Mutation Preparation Model

The application now models a controlled single-record mutation command for legacy semantic hash metadata. The command captures only safe facts:

- fiscal issuance reference identity
- latest recalculation preview audit identity
- controlled backfill approval basis
- stored and required semantic hash source versions
- recalculated hash value, algorithm, version, source fact count, and safe source summary
- actor/service identity
- approval and dual-control references
- correlation identity
- mutation mode `SingleRecordOnly`
- dry-run posture
- mutation status

The preparation service evaluates the latest controlled backfill approval posture and recalculation preview audit basis before returning a command posture. It blocks unless the record is still legacy `central-pms-pos-server-fiscal-request-v1`, the latest preview was successful and complete, the recalculated hash metadata is current `sha256:v1`, actor authorization and explicit approval are present, dual-control is satisfied when required, and no mismatch/manual-review/fiscal-conflict/closed/reconciled posture is present.

## Default Configuration Posture

Controlled mutation is disabled by default through `FiscalExceptionSemanticHashControlledBackfillMutationOptions`. When all modeled gates pass and mutation remains disabled, the service returns `PreparedButMutationDisabled` with a single-record command envelope and no fiscal reference mutation.

If controlled mutation is explicitly enabled in configuration, this slice still fails closed with `semantic_hash_controlled_backfill_guarded_mutation_not_implemented`. The actual guarded write method is intentionally deferred.

## Audit Persistence

A narrow append-only audit table, `core.fiscal_issuance_semantic_hash_backfill_mutation_preparations`, records mutation-preparation attempts. The repository stores:

- mutation audit identity
- fiscal issuance reference identity
- recalculation preview audit identity
- approval basis
- old and new semantic hash metadata
- mutation status and block reason
- mutation mode and enabled posture
- actor/service identity
- approval and dual-control references
- correlation identity
- attempted/created timestamps
- safe summary

The audit surface does not store raw POS Server payloads, payment provider payloads, secrets, customer PII, statutory evidence, or canonical source text. The audit table enforces `fiscal_issuance_reference_mutated = false` for this slice.

## Transaction And Safety Rules

The actual semantic hash metadata update remains deferred. A future guarded mutation slice must use a transaction, re-read the fiscal issuance reference before update, verify the row is still legacy, verify the approved preview audit basis and recalculated hash metadata, write audit evidence in the same transaction, update only semantic hash metadata fields, fail closed on stale row/source-version mismatch, and process only one fiscal issuance reference at a time.

## FEQ Detail Impact

FEQ read-only detail now exposes the controlled backfill mutation posture:

- mutation preparation status
- mutation block reason
- latest mutation audit basis
- attempted timestamp
- attempt count
- mutation mode
- mutation enabled/disabled posture
- safe summary

Legacy hashes remain blocked from retry readiness until a future approved mutation slice actually updates persisted semantic hash metadata to current `sha256:v1`. `RetryExecutionAvailable` remains false.

## Validation Coverage

Tests cover blocked preparation without ready approval, missing preview audit, missing actor/approval/dual-control posture, already-current or incompatible source versions, unsafe FEQ postures, disabled-mode dry-run preparation without mutation, audit persistence, FEQ detail projection of mutation posture, and retry readiness remaining blocked unless persisted semantic hash metadata is current.

## Remaining Blockers Before Retry Execution

- Implement and approve a guarded single-record semantic hash metadata mutation slice.
- Keep automatic batch backfill disabled unless separately designed, approved, and audited.
- Re-run retry eligibility after persisted semantic hash metadata is actually current.
- Keep controlled retry execution disabled until all FEQ scheduler, execution, POS Server readiness, authorization, and dual-control gates are satisfied.
