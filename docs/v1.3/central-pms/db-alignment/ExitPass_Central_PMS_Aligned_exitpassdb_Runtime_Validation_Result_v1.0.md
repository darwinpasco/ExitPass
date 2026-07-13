# ExitPass Central PMS Aligned exitpassdb Runtime Validation Result v1.0

## Result

PARTIAL.

Central PMS was validated against a fresh disposable database built from the canonical `exitpassdb_v1.2` generated SQL. The canonical schema applies cleanly and the v1.3 Central PMS alignment validation passes. Focused Central PMS tests for Management Platform identity/RBAC inventory, Operator Console access persistence/read repository, statutory discount payable-basis application, and statutory discount RBAC pass against the aligned database.

Full payment/fiscal runtime validation remains blocked because the canonical generated SQL contains zero-argument placeholder payment-chain routines instead of the typed routines called by Central PMS.

## Database Source

| Item | Value |
| --- | --- |
| DB repo generated SQL | `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` |
| DB repo validation SQL | `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql` |
| Disposable DB | `centralpms_aligned_exitpassdb_validation_local` |
| PostgreSQL | `localhost:5433`, Docker container `exitpass-postgres` |

## Database Validation

| Check | Result |
| --- | --- |
| Create disposable DB | Passed |
| Apply canonical generated SQL | Passed |
| Run `Validate-V13CentralPmsAlignment.sql` | Passed |

Validation output:

```text
ExitPass v1.3 Central PMS DB alignment schema validation passed.
```

## Focused Central PMS Tests

| Area | Command | Result |
| --- | --- | --- |
| Unit: statutory discount, RBAC, Management Platform inventory | `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~StatutoryDiscount|FullyQualifiedName~CentralPmsRbacPolicyCatalogTests|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests|FullyQualifiedName~ManagementPlatform"` | Passed: 128 |
| Integration: Management Platform inventory, Operator Console access persistence/read repository, statutory discount apply/RBAC | `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformIdentityRbacInventoryApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountRbacContractIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationPersistenceIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests"` | Passed: 41 |
| Central PMS API build | `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore` | Passed |

## UAT Seed and Fixture Validation

| Check | Result |
| --- | --- |
| Management Platform UAT identity/RBAC seed | Passed |
| Management Platform UAT identity/RBAC verify | Passed: 7 active UAT users, 7 active role bundles, 89 active permissions, 0 missing role-permission bindings, 0 missing user-role assignments |
| Statutory discount pilot fixture seed | Passed |
| Statutory discount pilot fixture verify | Passed: `E2E-231-SESSION-001`, active session, active original tariff snapshot, requester/reviewer distinct, no payment/gate/provider side effects |

## Canonical DB Gap Found

The generated canonical SQL currently includes placeholder routines:

```text
core.create_or_reuse_payment_attempt()
core.record_payment_confirmation()
core.issue_exit_authorization()
gates.consume_exit_authorization()
```

Central PMS calls typed routines such as:

```text
core.create_or_reuse_payment_attempt(uuid, uuid, text, text, text, uuid, timestamptz)
```

The focused fiscal issuance/payment-backed test run failed on:

```text
42883: function core.create_or_reuse_payment_attempt(uuid, uuid, text, text, text, uuid, timestamp with time zone) does not exist
```

This should be fixed in `exitpassdb_v1.2` by promoting the typed payment-chain routines into the canonical object-source output. The app-local patch `infra\db\patches\ExitPass_Core_CreateOrReusePaymentAttempt_v1.2.sql` remains retained but was not applied to this canonical validation database.

## App-Local Patches

Superseded by canonical DB output for this validation, retained as historical/app-local patch artifacts:

| Patch | Posture |
| --- | --- |
| `infra\db\patches\ExitPass_CentralPms_FiscalReferenceStatePersistence_v1.3.sql` | Superseded for canonical DB validation |
| `infra\db\patches\ExitPass_OperatorConsoleSchema_v1.2.sql` | Superseded for canonical DB validation |
| `infra\db\patches\ExitPass_ProductionPolicyImportReviewQueue_v1.2.sql` | Superseded for canonical DB validation |
| `infra\db\patches\ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql` | Superseded for canonical DB validation |
| `infra\db\patches\ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql` | Superseded for canonical DB validation |
| `infra\db\patches\ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql` | Partially superseded; canonical output uses the `sites.sites.lgu_code` bridge and does not include `sites.jurisdictions` |
| `infra\db\patches\ExitPass_Core_CreateOrReusePaymentAttempt_v1.2.sql` | Still needed by payment-chain tests until the typed routine is promoted to canonical DB output |

## Files Changed

| File | Change |
| --- | --- |
| `scripts/management-platform/Verify-ManagementPlatformUatIdentityRbac.sql` | Aligned verification with canonical `operator_console.operator_device_bindings.device_status` and `operator_console.operator_shifts.operational_status` columns |
| `src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\Api\OperatorConsoleAccessEvaluationPersistenceIntegrationTests.cs` | Aligned test readback with current `operations.operator_action_logs` access-evaluation persistence |
| `src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\Api\OperatorConsoleStatutoryDiscountLockedSchemaFixture.cs` | Aligned statutory discount fixture with canonical `sites.sites.lgu_code` policy-resolution bridge |
| `docs\v1.3\central-pms\db-alignment\ExitPass_Central_PMS_Aligned_exitpassdb_Runtime_Validation_Result_v1.0.md` | Added this result note |

## Validation Commands

```powershell
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "DROP DATABASE IF EXISTS centralpms_aligned_exitpassdb_validation_local WITH (FORCE);"
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "CREATE DATABASE centralpms_aligned_exitpassdb_validation_local;"
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d centralpms_aligned_exitpassdb_validation_local -f /tmp/exitpass-full-object.generated.sql
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d centralpms_aligned_exitpassdb_validation_local -f /tmp/Validate-V13CentralPmsAlignment.sql
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~StatutoryDiscount|FullyQualifiedName~CentralPmsRbacPolicyCatalogTests|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests|FullyQualifiedName~ManagementPlatform"
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformIdentityRbacInventoryApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountRbacContractIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationPersistenceIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests"
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore
```

## Remaining Gaps

- Promote typed payment-chain routines from app-local patches into canonical `exitpassdb_v1.2` object-source output.
- After the DB repo routine gap is fixed, rerun fiscal issuance reference/status/readback tests and payment/Sales Invoice handoff tests against a fresh canonical database without app-local patches.
- Keep existing app-local patch files until canonical DB consumers no longer need them for historical rebuild or targeted local validation.
