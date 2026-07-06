# FEQ Controlled Retry UAT - Scenario 1 Disabled By Default

Date: 2026-07-06
Branch: dev
Commit: a0f5063

## Environment

- Central PMS URL: http://localhost:5080
- POS Server URL: http://localhost:5000
- Disposable Central PMS DB: centralpms_feq_retry_uat_local
- Evidence folder: tmp/manual-smoke/central-pms-feq-controlled-retry-uat/evidence/scenario1-disabled-by-default-20260706-uat

## Invocation

No FEQ retry execution public or internal HTTP endpoint is exposed in the merged application. Scenario 1 was executed through the existing local worker test harness:

```powershell
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~FiscalExceptionControlledRetryExecutionServiceTests.ExecuteAsync_WhenDisabledByDefault_DoesNotCallPosServerPath" --no-restore --logger "console;verbosity=minimal"
```

Raw output: scenario1-disabled-worker-test.log

## Evidence Captured

- Feature flag state: `FiscalExceptionControlledRetryExecutionOptions.EnableControlledRetryExecution = false`
- Fiscal issuance reference id: generated in-memory by the existing test fixture; no persisted DB reference was created or selected
- Result/status: `Disabled`
- Block reason: `controlled_retry_execution_disabled`
- POS Server POST count: `0`, asserted by the test double via `DidNotReceiveWithAnyArgs`
- RetryExecutionAvailable: `false`
- Execution audit mutation: none; disabled path leaves the fake execution audit repository empty
- Fiscal reference success mutation: none

## Forbidden Side Effects

- Payment finality mutation: not performed
- ExitAuthorization issuance: not performed
- Gate behavior: not performed
- Fiscal number editing: not performed
- Manual fiscal document creation: not performed
- Batch retry: not performed
- Scheduler/background job: not performed
- POS Server POST: not performed

## Result

PASS for the disabled-by-default worker safety path.

Limitation: this was not an HTTP/API invocation because no FEQ controlled retry execution endpoint exists, and adding one is outside the UAT safety boundary.
