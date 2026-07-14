# ExitPass Central PMS Fiscal Before ExitAuthorization Hard Blocking Result v1.0

## Result

PASSED.

This slice wires the existing fiscal/payment readiness posture into the actual Central PMS ExitAuthorization issue path. The previous aligned-DB proof established the readiness/shadow model and recorded `EnforcementWiredForBlocking=False`; this slice changes the issue-time behavior so a blocking readiness result prevents `core.issue_exit_authorization` from being called.

## v1.3 Alignment

The v1.3 BRD and system design state that Central PMS owns payment finality, fiscal reference recording, and ExitAuthorization, that POS Server owns fiscal issuance, and that fiscal issuance must succeed before normal ExitAuthorization. This implementation follows that authority boundary and does not move payment, fiscal, gate, or POS authority into Operator Console or POS Server.

## Issue Path Changed

`IssueExitAuthorizationHandler` now performs a pre-issue gate before invoking the typed database routine:

- checks Central PMS payment finality through `IExitAuthorizationPaymentFinalityReadRepository`;
- evaluates fiscal issuance readiness through the existing `IExitAuthorizationFiscalGatingShadowEvaluator`;
- records the existing shadow/diagnostic observation;
- fails closed with `ExitAuthorizationIssuanceConflictException` when readiness would block and hard blocking is enabled;
- only calls `IIssueExitAuthorizationGateway.IssueAsync` when payment finality and fiscal readiness allow issuance.

Runtime DI now uses the PostgreSQL payment-finality read repository, and the default fiscal gating posture is hard blocking.

## Positive Issue Proof

Focused handler/API coverage proves that an ExitAuthorization may still be issued when:

- payment attempt and confirmation finality are verified;
- a safe fiscal issuance reference is recorded;
- the fiscal gating evaluator returns allow;
- the request is otherwise valid.

The aligned discounted payment and live POS Server Sales Invoice runtime proof remains the source of the local POS issuance proof. This slice did not rerun the live POS smoke.

## Replay Proof

Replay remains idempotent. Focused API and database-routine tests prove replaying an already issued valid ExitAuthorization returns the existing authorization and does not create a duplicate row or gate side effect.

## Missing Fiscal Block Proof

Issue-time blocking is now enforced when payment finality exists but no fiscal issuance reference is recorded.

Observed/covered reason:

- `fiscal_reference_not_recorded`

The focused API test asserts HTTP conflict behavior and verifies no ExitAuthorization row is created.

## Missing Payment Finality Block Proof

Issue-time blocking is now enforced before fiscal evaluation when Central PMS payment finality is not verified.

Observed/covered reason:

- `payment_finality_not_verified`

The focused handler and API tests assert the DB routine is not called and no ExitAuthorization is created.

## Unsafe Fiscal State Block Proof

Unsafe fiscal state remains fail-closed at issue time. Focused tests cover blocked fiscal readiness, conflict/failure/manual-review/exception-release postures, and lookup failure posture. These states now block before the authorization routine instead of remaining shadow-only.

Representative blocked reason:

- `fiscal_issuance_conflict`

## Safety Assertions

- No gate open command was introduced.
- No gate authorization consumption was introduced.
- No live payment provider call was introduced.
- No live HikCentral call was introduced.
- No refund/reversal behavior was introduced.
- No POS Server source change was made.
- No POS Server Sales Invoice creation was added to this enforcement slice.
- No fiscal number allocation outside POS Server was introduced.
- No final BIR rendering path was introduced.
- No raw evidence bytes were stored or read.

## Validation Commands

| Command | Result |
| --- | --- |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FiscalIssuanceExitAuthorizationGateEvaluatorTests\|FullyQualifiedName~FiscalIssuanceExitAuthorizationPreflightTests\|FullyQualifiedName~IssueExitAuthorizationHandlerTests\|FullyQualifiedName~PaymentToExitOperationalEvidenceTests" -m:1 /p:UseSharedCompilation=false` | PASSED, 114/114. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IssueExitAuthorizationApiIntegrationTests" -m:1 /p:UseSharedCompilation=false` | PASSED, 11/11. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IssueExitAuthorizationIntegrationTests" -m:1 /p:UseSharedCompilation=false` | PASSED, 4/4. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IssueExitAuthorizationApiIntegrationTests\|FullyQualifiedName~IssueExitAuthorizationIntegrationTests\|FullyQualifiedName~LocalRuntime_WhenEnabled_DiscountedPaymentAndFiscalIssuanceAreReadyForExitAuthorization" -m:1 /p:UseSharedCompilation=false` | PASSED, 16/16. The local live POS smoke remains opt-in and was not enabled in this run. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FiscalIssuanceExitAuthorizationGateEvaluatorTests\|FullyQualifiedName~FiscalIssuanceExitAuthorizationPreflightTests\|FullyQualifiedName~IssueExitAuthorizationHandlerTests\|FullyQualifiedName~PaymentToExitOperationalEvidenceTests\|FullyQualifiedName~FiscalIssuanceControlledUatEvidenceExporterTests" -m:1 /p:UseSharedCompilation=false` | PASSED, 132/132. |
| `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore /p:UseSharedCompilation=false` | PASSED. |
| `git diff --check` | PASSED. |
| `git status --short --branch --untracked-files=all` | Reviewed. Shows this slice's modified/new files plus an unrelated untracked assisted-payment-terminal assessment document left untouched. |

## Files Changed

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/ExitAuthorizationFiscalGatingShadowEvaluator.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceExitAuthorizationEnforcementDecision.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceExitAuthorizationGatingReadiness.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Payments/IExitAuthorizationPaymentFinalityReadRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Payments/IssueExitAuthorizationHandler.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Payments/ExitAuthorizationPaymentFinalityReadRepository.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/IssueExitAuthorizationApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Shared/PaymentTestDataHelper.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/IssueExitAuthorizationHandlerTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/PaymentToExitOperationalEvidenceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporterTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceExitAuthorizationGateEvaluatorTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceExitAuthorizationPreflightTests.cs`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Before_ExitAuthorization_Hard_Blocking_Result_v1.0.md`

## Remaining Gaps

- The full local live POS Server runtime proof was not rerun in this hard-blocking slice; the existing aligned-DB payment-to-Sales-Invoice and ExitAuthorization readiness runtime proofs remain the POS runtime evidence.
- Manual browser testing is not required for this backend enforcement slice.
