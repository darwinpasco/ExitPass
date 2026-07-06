# ExitPass FEQ Semantic Hash Backfill Internal API Prep Implementation Note v1.0

## Scope

This slice adds a locked-down internal API preparation surface for the governed single-record semantic hash backfill workflow.

It does not add public UI, public unauthenticated access, batch backfill, retry execution, retry worker, retry endpoint, POS Server POST, fiscal-gated ExitAuthorization changes, payment finality mutation, fiscal number editing, or manual fiscal document creation.

## Internal API Surface

Route:

- `POST /internal/v1/fiscal-exception-queue/semantic-hash-backfill-requests`

The endpoint is mapped through the existing Central PMS internal endpoint convention and requires `RequireInternalServiceMtls()`. It accepts one fiscal issuance reference only and delegates to the application handler `FiscalExceptionSemanticHashBackfillInternalApiHandler`.

## Default Configuration Posture

The application-level option `FiscalExceptionSemanticHashBackfillInternalApiOptions.Enabled` is disabled by default.

When disabled, the handler fails closed with:

- `semantic_hash_backfill_internal_api_disabled`

## Request/Response DTOs

The request model is single-record only and carries safe workflow inputs:

- fiscal issuance reference id
- recalculation preview audit id
- mutation-prep audit id
- approval reference
- dual-control reference
- actor/service identity
- reason code
- safe justification
- correlation id
- dry-run / controlled-mutation intent

The response returns safe workflow posture only:

- workflow request id
- workflow status
- block reason
- mutation invocation posture
- guarded mutation audit basis when invoked
- retry execution available: `false`
- safe summary

No raw POS Server payloads, payment provider payloads, secrets, canonical source text, PII, or statutory evidence are exposed.

## Workflow Integration

The handler resolves:

- FEQ read-only detail from `IFiscalExceptionQueueService`
- latest recalculation preview audit summary
- exact mutation-prep audit record by requested audit id

It rejects batch-shaped requests before workflow invocation, validates the requested preview and mutation-prep audit basis, reconstructs the persisted mutation-prep posture, and then calls `IFiscalExceptionSemanticHashBackfillOperatorWorkflowService`.

The workflow service remains the only path that can invoke guarded mutation, and only when both API and workflow configuration allow it.

## Remaining Blocked

Retry execution remains unavailable. No retry worker, executable scheduler/job, retry endpoint, POS Server POST, payment finality mutation, ExitAuthorization, gate behavior, fiscal number editing, or manual fiscal document creation is introduced.

