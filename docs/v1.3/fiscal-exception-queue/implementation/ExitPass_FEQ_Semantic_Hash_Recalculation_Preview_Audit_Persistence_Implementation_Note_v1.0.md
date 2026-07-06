# ExitPass FEQ Semantic Hash Recalculation Preview Audit Persistence Implementation Note v1.0

## Scope

This slice persists audit records for Central PMS semantic hash recalculation preview attempts.

The persistence path remains non-mutating. It does not update `fiscal_issuance_references` semantic hash metadata, does not backfill historical hashes, and does not make legacy records retry-safe.

## Persistence Approach

A narrow append-only audit table was added:

- `core.fiscal_issuance_semantic_hash_recalculation_previews`

The table is accessed through:

- `IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository`
- `PostgresFiscalExceptionSemanticHashRecalculationPreviewAuditRepository`

The repository supports:

- recording a preview audit attempt
- reading the latest safe summary for FEQ detail

No raw POS Server payload, canonical source text, payment provider payload, secret, customer PII, or statutory evidence is stored.

## Audit Fields Persisted

The audit record stores safe facts only:

- recalculation preview audit id
- fiscal issuance reference id
- stored semantic hash source version
- required semantic hash source version
- stored semantic hash value
- recalculation preview status
- recalculation block reason
- complete original request facts availability
- recalculated hash value only for complete valid facts
- recalculated hash algorithm and source version
- recalculated source fact count
- safe source summary
- comparison result versus stored hash
- mutation status
- attempted/created timestamps
- correlation id
- actor/service identity id
- safe summary

## Preview Flow

`FiscalExceptionSemanticHashRecalculationPreviewService` now exposes an audited async path.

When an audit repository is supplied and the preview is tied to a fiscal issuance reference, preview attempts are persisted. If audit persistence fails, the service throws `semantic_hash_recalculation_preview_audit_persistence_failed` rather than returning a result that appears durably auditable.

The existing pure preview path remains available for non-persistent inspection and FEQ read-only default posture.

## FEQ Detail Impact

FEQ detail can now overlay the latest recalculation preview audit summary:

- last recalculation preview status
- last attempted timestamp
- attempt count
- last block reason
- mutation status
- safe summary

This is read-only. It does not alter retry eligibility, command preparation, scheduling preparation, or execution preparation.

## Non-Mutation Guarantee

The implementation does not call `RecordSemanticRequestHashAsync` and does not update persisted semantic hash fields on `fiscal_issuance_references`.

Legacy `central-pms-pos-server-fiscal-request-v1` records remain blocked by `semantic_hash_legacy_version_requires_recalculation` after preview audit persistence.

`RetryExecutionAvailable` remains false.

## Validation

Validation performed:

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FiscalIssuance|FiscalExceptionQueue" --no-restore --logger "console;verbosity=minimal"` - passed, 409 tests.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FiscalIssuanceReferenceRepositoryTests|FiscalExceptionQueue|SemanticRequestHash|Recalculation" --no-restore --logger "console;verbosity=minimal"` - passed, 12 tests.

## Remaining Blockers Before Retry Execution

- legacy hashes still require a separately approved recalculation/backfill mutation workflow
- retry execution remains disabled and unimplemented
- no retry worker, executable scheduler/job, or retry endpoint exists
- POS Server retry execution readiness remains a future controlled slice concern

