# ExitPass Central PMS Aligned exitpassdb Runtime Validation Result v1.0

## Result

PASSED.

Central PMS was validated against a fresh disposable database built from the canonical `exitpassdb_v1.2` generated SQL. The prior PARTIAL blocker is closed: canonical `exitpassdb_v1.2` now includes the typed Central PMS payment-chain routines required by current source.

## Database Source

| Item | Value |
| --- | --- |
| DB repo branch | `develop`, pulled from `origin/develop`, already up to date |
| DB repo generated SQL | `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` |
| DB repo validation SQL | `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql` |
| Disposable DB | `centralpms_aligned_exitpassdb_validation_local` |
| PostgreSQL | `localhost:5433`, Docker container `exitpass-postgres` |
| Central PMS test connection | `Host=localhost;Port=5433;Database=centralpms_aligned_exitpassdb_validation_local;Username=exitpass;Password=change_me;Include Error Detail=true` |

## Database Validation

| Check | Result |
| --- | --- |
| Create disposable DB | Passed |
| Apply canonical generated SQL | Passed |
| Run `Validate-V13CentralPmsAlignment.sql` | Passed |
| Typed payment-chain routine assertions | Passed |

Validation output:

```text
ExitPass v1.3 Central PMS DB alignment schema validation passed.
```

## Typed Payment-Chain Validation

Canonical DB output now exposes these typed routines without applying app-local payment-chain patches:

| Routine | Signature |
| --- | --- |
| `core.create_or_reuse_payment_attempt` | `(uuid, uuid, text, text, text, uuid, timestamptz)` |
| `core.finalize_payment_attempt` | `(uuid, text, text, uuid, timestamptz)` |
| `core.record_payment_confirmation` | `(uuid, text, text, text, uuid, timestamptz)` |
| `core.issue_exit_authorization` | `(uuid, uuid, uuid, uuid, timestamptz)` |
| `core.consume_exit_authorization` | `(uuid, uuid, uuid, timestamptz)` |

| Test group | Result |
| --- | --- |
| `CreateOrReusePaymentAttemptDbRoutineGatewayTests` | Passed |
| `CreateOrReusePaymentAttemptDbRoutineGatewayConcurrencyTests` | Passed |
| `FinalizePaymentAttemptIntegrationTests` | Passed |
| `RecordPaymentConfirmationIntegrationTests` | Passed |
| `IssueExitAuthorizationIntegrationTests` | Passed |
| `ConsumeExitAuthorizationIntegrationTests` | Passed |

## Fiscal Reference / Status / Readback Validation

| Test group | Result |
| --- | --- |
| `FiscalIssuanceReferenceRepositoryTests` | Passed: 14 |
| `FiscalIssuanceStatusReadServiceTests` and `FiscalExceptionReadbackWorkerTests` | Passed: 29 |

## Prior Focused Central PMS Tests

| Area | Command | Result |
| --- | --- | --- |
| Unit: statutory discount, RBAC, Management Platform inventory | `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~StatutoryDiscount|FullyQualifiedName~CentralPmsRbacPolicyCatalogTests|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests|FullyQualifiedName~ManagementPlatform"` | Passed: 128 |
| Integration: Management Platform inventory, Operator Console access persistence/read repository, statutory discount apply/RBAC | `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformIdentityRbacInventoryApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountRbacContractIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationPersistenceIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests"` | Passed: 41 |
| Central PMS API build | `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore` | Passed |

## UAT Seed and Fixture Validation

The wrapper scripts remain intentionally pinned to `exitpass_v12_dev`; for this canonical validation, their seed/verify SQL files were run directly against `centralpms_aligned_exitpassdb_validation_local` with only the database-name safety guard adapted in-memory.

| Check | Result |
| --- | --- |
| Management Platform UAT identity/RBAC seed | Passed |
| Management Platform UAT identity/RBAC verify | Passed: 7 active UAT users, 7 active role bundles, 89 active permissions, 0 missing role-permission bindings, 0 missing user-role assignments |
| Statutory discount pilot fixture seed | Passed |
| Statutory discount pilot fixture verify | Passed: `E2E-231-SESSION-001`, active session, active original tariff snapshot, payable amount `12500`, requester/reviewer distinct, no payment/provider/gate/coupon/reconciliation side effects |

## App-Local Patch Posture

Canonical DB validation no longer needs these promoted typed payment-chain patches:

| Patch | Posture |
| --- | --- |
| `infra\db\patches\ExitPass_Core_CreateOrReusePaymentAttempt_v1.2.sql` | Promoted to canonical DB output; retained as historical/app-local patch artifact |
| `infra\db\patches\ExitPass_Core_FinalizePaymentAttempt_v1.2.sql` | Promoted to canonical DB output; retained as historical/app-local patch artifact |
| `infra\db\patches\ExitPass_Core_RecordPaymentConfirmation_v1.2.sql` | Promoted to canonical DB output; retained as historical/app-local patch artifact |
| `infra\db\patches\ExitPass_Core_IssueExitAuthorization_v1.2.sql` | Promoted to canonical DB output; retained as historical/app-local patch artifact |
| `infra\db\patches\ExitPass_Core_ConsumeExitAuthorization_v1.2.sql` | Promoted to canonical DB output; retained as historical/app-local patch artifact |

Other previously superseded app-local patches remain retained as historical/app-local patch artifacts until a separate cleanup decision.

## Files Changed

| File | Change |
| --- | --- |
| `docs\v1.3\central-pms\db-alignment\ExitPass_Central_PMS_Aligned_exitpassdb_Runtime_Validation_Result_v1.0.md` | Updated validation result from PARTIAL to PASSED and recorded the typed payment-chain closure |

## Validation Commands

```powershell
git -C D:\SourceCodes\exitpassdb_v1.2 switch develop
git -C D:\SourceCodes\exitpassdb_v1.2 pull origin develop

docker cp D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql exitpass-postgres:/tmp/exitpass-full-object.generated.sql
docker cp D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql exitpass-postgres:/tmp/Validate-V13CentralPmsAlignment.sql
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "DROP DATABASE IF EXISTS centralpms_aligned_exitpassdb_validation_local WITH (FORCE);"
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "CREATE DATABASE centralpms_aligned_exitpassdb_validation_local;"
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d centralpms_aligned_exitpassdb_validation_local -f /tmp/exitpass-full-object.generated.sql
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d centralpms_aligned_exitpassdb_validation_local -f /tmp/Validate-V13CentralPmsAlignment.sql

$env:EXITPASS_TEST_MAIN_DB="Host=localhost;Port=5433;Database=centralpms_aligned_exitpassdb_validation_local;Username=exitpass;Password=change_me;Include Error Detail=true"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~CreateOrReusePaymentAttemptDbRoutineGatewayTests|FullyQualifiedName~CreateOrReusePaymentAttemptDbRoutineGatewayConcurrencyTests|FullyQualifiedName~FinalizePaymentAttemptIntegrationTests|FullyQualifiedName~RecordPaymentConfirmationIntegrationTests|FullyQualifiedName~IssueExitAuthorizationIntegrationTests|FullyQualifiedName~ConsumeExitAuthorizationIntegrationTests"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~FiscalIssuanceReferenceRepositoryTests"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FiscalIssuanceStatusReadServiceTests|FullyQualifiedName~FiscalExceptionReadbackWorkerTests"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~StatutoryDiscount|FullyQualifiedName~CentralPmsRbacPolicyCatalogTests|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests|FullyQualifiedName~ManagementPlatform"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformIdentityRbacInventoryApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountRbacContractIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationPersistenceIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests"
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore
```

## Remaining Gaps

- No blocking canonical DB alignment gaps remain from this validation pass.
- Historical app-local DB patches remain retained until a separate cleanup decision.
- Browser/operator runtime validation was not run because this slice is backend/database validation only.
