# ExitPass Central PMS POS Server Controlled UAT Fiscal Reference State Fix v1.0 Review

## Branch

`feature/central-pms-controlled-uat-fiscal-reference-state-fix`

## Purpose summary

Fix the failed controlled Central PMS to POS Server UAT `/run` attempt where the run path reached the live diagnostic orchestration path but failed with:

`Fiscal issuance reference state transition returned no rows.`

The fix prepares a Central PMS fiscal issuance reference before the controlled UAT harness invokes the live diagnostic path.

## Root cause

The controlled UAT invocation service built a harness request with a placeholder fiscal issuance reference ID. No matching active `core.fiscal_issuance_references` row existed in Central PMS, so `PostgresFiscalIssuanceReferenceRepository.UpdateStateAsync(...)` could not transition the reference to `FiscalIssuanceRequested` and returned no rows.

## Files changed

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatInvocationService.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatInvocationServiceTests.cs`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Invocation_Surface_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Fiscal_Reference_State_Fix_v1.0_Review.md`

## Fiscal reference preparation behavior

The run path now:

- validates request, approval, scenario, guard, totals, and sensitive-marker posture first;
- resolves an existing active fiscal issuance reference by upstream finality reference, Site POS Server ID, and fiscal document type ID when available;
- accepts only startable existing reference states for this first controlled run path;
- creates a pending fiscal issuance reference through `IFiscalIssuanceOrchestrationService.PreparePendingAsync(...)` when no existing reference is found;
- uses the persisted reference ID returned by Central PMS when invoking `IFiscalIssuanceControlledUatHarness`;
- returns a controlled `fiscal_reference_prepare_failed` response if creation/resolution fails;
- returns a controlled `fiscal_reference_prepare_rejected` response if an existing reference is not startable;
- does not bypass the state machine or remove transition checks.

Preflight remains non-mutating and does not create fiscal issuance reference rows.

## Safety boundaries preserved

- Payment finality is not mutated by the invocation service.
- ExitAuthorization is not issued by the invocation service.
- Gate behavior is not triggered.
- Fiscal gating enforcement remains disabled.
- Evidence files are not written automatically.
- Payment and exit production flows are not wired to POS Server calls.
- The invocation surface remains internal controlled-UAT-only.

## Non-goals preserved

- No POS Server runtime repository changes.
- No SQL or migration changes.
- No manual Central PMS SQL seeding as the normal fix.
- No `/run` endpoint call executed by this task.
- No fiscal document created by this task.
- No retry scheduler or GET readback worker added.
- No Operator Console or Dashboard implementation added.

## Tests added or updated

Updated `FiscalIssuanceControlledUatInvocationServiceTests` to cover:

- run path creates a pending fiscal issuance reference before invoking the harness;
- run path reuses an existing startable pending reference;
- run path does not call the harness if reference preparation fails;
- run path returns a controlled reference-preparation error;
- run path rejects an existing non-startable recorded reference before the harness;
- happy path invokes the controlled UAT harness once with the persisted reference ID;
- happy path still returns safe evidence JSON;
- happy path still reports no payment finality, ExitAuthorization, gate, fiscal gating, or file-writing side effects;
- preflight remains non-mutating and does not create/resolve fiscal references.

## Validation results

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter FiscalIssuance --no-restore --logger "console;verbosity=minimal"`: passed, 263 passed.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore --verbosity minimal`: passed with 0 warnings and 0 errors.
- `git diff --check`: passed.
- `git status --short --untracked-files=all`: expected Central PMS source/test/docs changes plus pre-existing unrelated untracked `infra/docker/docker-compose.controlled-uat.override.yml`.
- Changed-file obsolete primary terminology search: no matches.
- `git -c safe.directory=D:/SourceCodes/ExitPass-PoSServer -C D:\SourceCodes\ExitPass-PoSServer status --short --untracked-files=all`: clean.

## Runtime retry note

The failed first `/run` response is not successful UAT evidence and must not be reused as success evidence.

Actual controlled UAT execution still must be repeated manually after merge, after the Central PMS container is rebuilt, after dry-run checks pass again, and after Darwin explicitly approves the retry.

## Remaining runtime checks

- Rebuild/restart Central PMS container with this fix.
- Confirm controlled UAT config guards remain correct.
- Confirm Central PMS database contains the approved development payment/session rows required by fiscal reference FK constraints.
- Confirm POS Server fixture rows still exist.
- Confirm evidence folder is ready.
- Re-run preflight.
- Request Darwin approval before retrying `/run`.

## Final implementation status

`controlled_invocation_surface_available_reference_state_fixed_pending_run_retry`

This is not `ready_for_execution`.

## Recommended next step

Rebuild the Central PMS container and retry `/run` once after approval.
