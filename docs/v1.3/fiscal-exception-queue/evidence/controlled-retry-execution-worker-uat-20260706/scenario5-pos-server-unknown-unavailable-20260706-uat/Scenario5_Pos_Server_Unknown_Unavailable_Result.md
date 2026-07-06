# FEQ Controlled Retry UAT - Scenario 5 POS Server Unknown or Unavailable

Date: 2026-07-06
Branch: dev
Commit: a0f5063

## Environment

- Central PMS URL: http://localhost:5080
- POS Server URL: http://localhost:5000
- Disposable Central PMS DB: centralpms_feq_retry_uat_local
- Evidence folder: tmp/manual-smoke/central-pms-feq-controlled-retry-uat/evidence/scenario5-pos-server-unknown-unavailable-20260706-uat

## Invocation

No FEQ retry execution HTTP/API endpoint is exposed in the merged application. Scenario 5 was executed through the existing local worker test harness:

```powershell
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~FiscalExceptionControlledRetryExecutionServiceTests.ExecuteAsync_WhenPosServerOutcomeUnknown_DoesNotRetryAgain" --no-restore --logger "console;verbosity=minimal"
```

Raw output: scenario5-unknown-unavailable-worker-test.log

## Evidence Captured

- Feature flag state: controlled retry execution enabled for this scenario by the worker test fixture
- Fiscal issuance reference id: generated in-memory by the existing test fixture; not emitted by the current harness and not persisted
- Upstream finality reference: `upstream-finality-ref`
- Semantic hash value: `6a490379e4275a57f0a0695ff9dbd1271c4480adaeeefb9b6bfbd11e4d1ed201`
- Semantic hash source version: `sha256:v1`
- Readback basis: latest classification `not_found`, durable readback attempt count `1`
- Worker status: `Unknown`
- Worker summary: contains `requires_readback`
- POS Server integration path: fake `IFiscalIssuancePosServerLiveIntegrationService`
- POS Server POST count: `1`, asserted by `Received(1)`
- POS Server outcome: `FailedService`
- POS Server result classification/posture: `pos_server_timeout` with `RetryAfterServiceRecovery`
- Retry execution audit: enabled path uses the fake execution audit repository; audit id is not printed by the existing harness
- Fiscal reference update posture: no fiscal reference success mutation; fake integration result returns unknown state
- Future readback requirement: required before any future retry attempt
- RetryExecutionAvailable: `false`
- Retry loop: not performed
- DB/API mutation: none; this worker-level harness used in-memory fakes only

## Forbidden Side Effects

- Payment finality mutation: not performed
- ExitAuthorization issuance: not performed
- Gate behavior: not performed
- Fiscal number editing: not performed
- Manual fiscal document creation: not performed
- Batch retry: not performed
- Scheduler/background job: not performed
- Public retry endpoint: not used

## Result

PASS for the POS Server unknown/unavailable worker path.

Limitation: this was not an HTTP/API invocation and did not call a live POS Server because no FEQ controlled retry execution endpoint exists, and adding one is outside the UAT safety boundary.
