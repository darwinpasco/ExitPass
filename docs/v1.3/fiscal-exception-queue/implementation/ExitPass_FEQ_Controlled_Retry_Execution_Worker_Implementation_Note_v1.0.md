# ExitPass FEQ Controlled Retry Execution Worker Implementation Note v1.0

## Scope

This slice adds the smallest controlled Central PMS FEQ retry execution service for one fiscal issuance reference at a time. It remains disabled by default and does not add a public endpoint, batch retry, scheduler job, Operator Console UI, Management Dashboard projection, ExitAuthorization issuance, or gate behavior.

## Behavior

- Added `FiscalExceptionControlledRetryExecutionService`.
- Added `FiscalExceptionControlledRetryExecutionOptions` with `EnableControlledRetryExecution = false` by default.
- The service accepts a single FEQ detail, command-prep basis, scheduling-prep basis, execution-prep basis, original fiscal request mapping context, service identity, approval reference, and dual-control reference.
- When disabled, it returns `Disabled` without calling the POS Server path.
- When enabled, it requires all modeled FEQ retry gates and validates that the supplied original fiscal request facts still produce the persisted `sha256:v1` semantic request hash.

## Gates Enforced

- Single-record request only.
- Latest readback classification is `not_found`.
- Durable readback attempt exists.
- Retry eligibility is eligible for controlled retry planning.
- Retry command preparation is `PreparedNonExecutable` and has an audit id.
- Retry scheduling preparation is `ScheduledPrepared` and has an audit id.
- Retry execution preparation is `ReadyForExecutionWhenEnabled`.
- POS Server readiness gates are confirmed by execution-prep.
- Semantic request hash is current `sha256:v1`, available, and matches the mapped request facts.
- Upstream finality/idempotency reference is unchanged.
- Service identity, approval reference, and required dual-control reference are present.
- Unsafe queue states remain blocked.
- Audit persistence must be available when controlled execution is enabled.

## POS Server POST Path

The service calls POS Server only through the existing `IFiscalIssuancePosServerLiveIntegrationService.TryIssueFiscalDocumentViaPosServerAsync` path. That path owns request mapping, semantic hash recording, `IPosServerFiscalDocumentClient.CreateFiscalDocumentAsync`, and applying POS Server results through fiscal issuance orchestration.

## Audit

Added append-only `core.fiscal_issuance_retry_execution_attempts` and `IFiscalExceptionControlledRetryExecutionAuditRepository`.

The audit stores safe facts only: fiscal issuance reference id, command/schedule audit basis, readback/hash/idempotency basis, execution status, POS Server outcome/classification/evidence fields when returned, timestamps, actor/service identity, correlation id, and safe summary. It does not store raw POS Server payloads, payment payloads, secrets, PII, statutory evidence, or canonical source text.

## Result Handling

- Accepted newly-created evidence returns `Executed`.
- Accepted same-key/same-hash replay returns `ReplayMatched`.
- POS Server idempotency conflict returns `Conflict` and does not loop.
- Unknown/service failure returns `Unknown` and relies on readback before any future retry.
- No automatic second retry exists.

## Fields Updated

The worker itself does not edit payment finality, ExitAuthorization, gate state, fiscal numbers, or manual fiscal documents. Fiscal reference evidence/status is updated only by the existing fiscal issuance orchestration path when POS Server returns durable success/replay evidence.

## Remaining Blockers

- Controlled execution remains disabled unless explicitly configured.
- No public endpoint, scheduler, batch executor, or operator UI exists.
- Operational enablement still needs deployment configuration, runbook controls, and production authorization governance.
