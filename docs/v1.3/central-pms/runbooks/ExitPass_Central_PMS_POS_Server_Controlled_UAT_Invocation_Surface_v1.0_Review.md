# ExitPass Central PMS POS Server Controlled UAT Invocation Surface v1.0 Review

## Branch name

`feature/central-pms-pos-server-controlled-uat-invocation-surface`

## Files changed

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatInvocationService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/InternalControlledUatFiscalIssuanceEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatInvocationServiceTests.cs`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Dry_Run_Checklist_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Invocation_Surface_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Invocation_Surface_v1.0_Review.md`

## Purpose summary

Implemented the smallest guarded internal Central PMS invocation surface for the existing controlled UAT fiscal issuance harness.

## Endpoint path added

- `POST /internal/controlled-uat/fiscal-issuance/run`

## Preflight endpoint

Added:

- `POST /internal/controlled-uat/fiscal-issuance/preflight`

The preflight endpoint validates request shape, approval fields, config guards, totals, sensitive markers, and safety posture. It does not call POS Server and does not create a fiscal document.

## Config guards enforced

- `EnableControlledUatDiagnosticPath = true`
- `EnablePosServerFiscalIssuanceLiveCall = true`
- `PosServerBaseUrl` present and valid
- `EnableLiveFiscalIssuanceFromPaymentFlow = false`
- `EnableLiveFiscalIssuanceFromExitFlow = false`
- `EnableFiscalBeforeExitAuthorizationEnforcement = false`

## Approval fields required

- run ID
- approval reference
- approved by
- `explicitExecutionApproval = true`
- correlation ID

## Safety guarantees

Responses explicitly report:

- `paymentFinalityChanged = false`
- `exitAuthorizationIssued = false`
- `gateBehaviorTriggered = false`
- `fiscalGatingEnforcementEnabled = false`
- `evidenceFileWritten = false`

The implementation does not add payment confirmation wiring, ExitAuthorization wiring, gate behavior, automatic evidence file writing, retry scheduling, GET readback worker behavior, Operator Console queues, or Dashboard projections.

## Tests added

Added `FiscalIssuanceControlledUatInvocationServiceTests` covering:

- controlled diagnostic flag disabled rejection;
- live-call seam disabled rejection;
- missing POS Server base URL rejection;
- payment-flow guard rejection;
- exit-flow guard rejection;
- fiscal gating enforcement rejection;
- missing/false explicit execution approval rejection;
- missing approval reference rejection;
- missing approved-by rejection;
- wrong fiscal document type rejection;
- replay/conflict/failure/unknown first-run rejection;
- totals mismatch rejection;
- sensitive marker rejection;
- happy path invokes harness once;
- happy path returns safe evidence JSON;
- happy path reports no payment, exit, gate, fiscal gating, or file-writing side effects;
- preflight does not invoke harness and returns no evidence.

## Test results

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter FiscalIssuance --no-restore --logger "console;verbosity=minimal"`: passed, 259 passed.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore --verbosity minimal`: passed with 0 warnings and 0 errors.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --logger "console;verbosity=minimal"`: failed due to unrelated existing tests:
  - `OperatorConsoleAccessEvaluationWriterTests.WriterSource_MapsEvaluationAndDenialReasonColumns`
  - `OperatorConsoleAccessEvaluationReadRepositoryTests.RepositorySource_UsesParameterizedQueries`
  - `WebPayPayMongoReconciliationDiagnosticsTests.PayMongoStatusQueryRuntimePath_IsNotImplementedAsPlatformFinalityPath`
- `git diff --check`: passed.
- `git status --short --untracked-files=all`: only intended Central PMS code and runbook files changed.
- Obsolete primary terminology search across changed files: no matches.

## Docs updated/created

- Updated the execution dry-run checklist from invocation-blocked to controlled invocation surface available pending pre-execution checks.
- Created this invocation surface runbook.
- Created this companion review.

## Remaining runtime checks

- Start POS Server on `http://localhost:8091`.
- Verify Central PMS Docker connectivity to `http://host.docker.internal:8091`.
- Verify Central PMS config/guard values.
- Verify POS Server dev fiscal identity, sequence policy, sequence state, and document type rows.
- Create/verify evidence folder.
- Run preflight endpoint.
- Capture Darwin explicit approval before calling the run endpoint.

## Confirmations

- No POS Server runtime files changed.
- No SQL or migration changes made.
- No automatic evidence writer added.
- No payment/exit/gate wiring added.
- No fiscal gating enforcement enabled.
- No UAT execution performed.
- No live POS Server fiscal document created.

## Final implementation status

`controlled_invocation_surface_available_pending_pre_execution_checks`

Not:

`ready_for_execution`

## Recommended next task

`manual controlled UAT pre-execution check run`

Purpose:

Run the dry-run checklist manually, capture runtime evidence for POS Server startup, Central PMS Docker connectivity, config/guard values, POS Server fiscal rows, evidence folder readiness, and then request Darwin's explicit approval before calling the controlled UAT run endpoint.
