# ExitPass FEQ Semantic Hash Recalculation Audit Prep Implementation Note v1.0

## Scope

This slice adds a Central PMS-only semantic hash recalculation preview path for fiscal issuance references that still store the legacy hash source version `central-pms-pos-server-fiscal-request-v1`.

The path is preview/audit preparation only. It does not mutate historical fiscal issuance reference records and does not make legacy records retry-safe.

## Recalculation Preview Policy

`FiscalExceptionSemanticHashRecalculationPreviewService` evaluates a fiscal issuance reference plus optional complete original fiscal request facts.

The service returns:

- preview status
- safe block reason
- stored source version
- required source version
- complete-facts availability
- recalculated hash value, algorithm, source version, fact count, and safe summary only when complete facts are supplied and valid
- comparison with the stored legacy hash when a preview hash can be calculated
- mutation status `NotMutated`

Legacy hashes without complete original fiscal request facts return:

- status: `Blocked`
- reason: `original_fiscal_request_facts_unavailable`
- summary: `semantic_hash_recalculation_preview_required_original_facts_unavailable`

Incomplete reconstructed facts return a blocked preview and do not produce a fake hash.

## Original Request Reconstruction

Complete original request reconstruction is not implemented as an automatic repository-backed flow in this slice. The preview service can calculate a current `sha256:v1` hash only when the caller supplies a complete `PosServerFiscalDocumentCreateRequest` that passes the existing semantic hash calculator completeness checks.

Partial facts are rejected. No facts are inferred or invented.

## Persistence And Audit

No new persistence table or repository was added in this slice. The existing FEQ audit surfaces cover readback, command preparation, and scheduling preparation, but none is a narrow fit for semantic hash recalculation preview without overloading retry command/scheduling history.

Audit persistence is deferred to a future slice that can add a narrow recalculation-preview audit surface if required.

## FEQ Detail Posture

FEQ case summaries now expose safe recalculation preview posture:

- `SemanticHashRecalculationPreviewStatus`
- `SemanticHashRecalculationPreviewBlockReasonCode`
- `SemanticHashRecalculationPreviewStoredSourceVersion`
- `SemanticHashRecalculationPreviewRequiredSourceVersion`
- `SemanticHashRecalculationPreviewAttemptedAt`
- `SafeSemanticHashRecalculationPreviewSummary`
- `SemanticHashRecalculationMutationStatus`

The default FEQ detail posture does not reconstruct original fiscal request facts. For legacy records, it therefore reports the preview as blocked by `original_fiscal_request_facts_unavailable` and `NotMutated`.

## Readiness Impact

Retry readiness remains blocked for legacy semantic hashes. The preview result is informational only and does not update persisted hash metadata.

The following remain blocked for legacy `central-pms-pos-server-fiscal-request-v1` records:

- retry eligibility
- retry command preparation
- retry scheduling preparation
- retry execution preparation

`RetryExecutionAvailable` remains false.

## Intentional Non-Implementation

This slice does not implement:

- automatic historical hash backfill mutation
- recalculation preview persistence
- retry execution
- retry worker
- executable scheduler/job
- retry endpoint
- POS Server POST from FEQ
- POS Server repository changes
- fiscal-gated ExitAuthorization enforcement
- payment finality mutation
- ExitAuthorization or gate behavior changes
- fiscal number editing
- manual fiscal document creation

## Validation

Validation performed:

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FiscalExceptionSemanticHashRecalculationPreviewServiceTests" --no-restore --logger "console;verbosity=minimal"`
- `git diff --check`
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FiscalIssuance|FiscalExceptionQueue" --no-restore --logger "console;verbosity=minimal"` - passed, 404 tests.
