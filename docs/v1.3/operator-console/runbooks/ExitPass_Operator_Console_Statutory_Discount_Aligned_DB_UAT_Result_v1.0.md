# ExitPass Operator Console Statutory Discount Aligned DB UAT Result v1.0

## Result

READY_WITH_ENV_PROFILE_IDENTITY_SWITCH.

The aligned canonical database path is ready for Darwin manual browser rerun. The backend/API proof completed the v1.3 two-user statutory discount UAT flow against a fresh database built from canonical `exitpassdb_v1.2` generated SQL. The current Operator Console UI does not expose an in-app requester/reviewer identity switch; local browser UAT must restart or run the UI with the printed requester/reviewer Vite environment profiles.

## Database Source

Canonical DB generated SQL:

`D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`

Canonical validation SQL:

`D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`

Disposable UAT DB:

`centralpms_operator_uat_aligned_local`

## UAT Users And Roles

| Actor | User | User ID | Role posture |
| --- | --- | --- | --- |
| Requester/evidence actor | `uat-operator-support` | `77000000-0000-0000-0000-000000000010` | Operator / Support Staff |
| Reviewer/apply actor | `uat-operations-supervisor` | `77000000-0000-0000-0000-000000000012` | Operations Supervisor |

The requester and reviewer are distinct identities. The requester can perform lookup, draft creation, and metadata-only evidence capture. The requester cannot approve their own statutory discount. The reviewer can approve and apply payable basis.

## Preflight Result

PASSED.

Preflight script:

`scripts\operator-console\Invoke-StatutoryDiscountOperatorUatAlignedDbPreflight.ps1`

The preflight rebuilds `centralpms_operator_uat_aligned_local` from canonical generated SQL, runs `Validate-V13CentralPmsAlignment.sql`, seeds/verifies the Management Platform UAT identity/RBAC model, seeds/verifies the statutory discount pilot fixture, and prints runtime/browser commands only after checks pass.

Verified fixture:

| Field | Value |
| --- | --- |
| Ticket reference | `E2E-231-SESSION-001` |
| Parking session ID | `23100000-0000-0000-0000-000000000003` |
| Original tariff snapshot ID | `23100000-0000-0000-0000-000000000004` |
| Site group ID | `77000000-0000-0000-0000-000000000001` |
| Site ID | `77000000-0000-0000-0000-000000000002` |
| Current payable | `12500` / PHP 125.00 |
| Currency | PHP |

## API Workflow Proof Result

PASSED.

Focused integration proof:

`OperatorConsoleStatutoryDiscountAlignedDbUatApiIntegrationTests.AlignedDbUatFixture_CompletesReviewApproveApplyWithoutUnsafeSideEffects`

Proof covered:

1. Session lookup returned `sessionFound=true`, `sessionEligible=true`, ticket `E2E-231-SESSION-001`, amount `12500`, currency `PHP`.
2. `uat-operator-support` created a Senior Citizen statutory discount draft.
3. Metadata-only evidence capture succeeded.
4. Same requester approval was denied with `REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT`.
5. Validation remained unapproved after the denied same-requester attempt.
6. `uat-operations-supervisor` approved the validation.
7. Apply payable basis succeeded.
8. PayableBasisApplicationId and applied tariff snapshot ID were non-null.
9. Final readback showed approved, evidence satisfied, payable basis applied.
10. Unsafe side-effect row counts did not change.

## Amount Proof

| Amount | Minor units | Display |
| --- | ---: | --- |
| Original gross | `12500` | PHP 125.00 |
| VAT-exclusive | `11161` | PHP 111.61 |
| VAT | `1339` | PHP 13.39 |
| Statutory discount | `2232` | PHP 22.32 |
| Final payable | `8929` | PHP 89.29 |

## Browser Readiness

Status: READY_WITH_ENV_PROFILE_IDENTITY_SWITCH.

The browser can execute the workflow with local/dev operator identity profiles. The UI currently reads the local operator identity and permission posture from Vite environment variables at startup. It does not yet provide an in-app local UAT identity selector.

Use the requester profile for ticket lookup, draft creation, and evidence capture. Use the reviewer/apply profile for approval and payable-basis application.

### Start Central PMS

```powershell
cd D:\SourceCodes\ExitPass
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:56065"
$env:ConnectionStrings__MainDatabase="Host=localhost;Port=5433;Database=centralpms_operator_uat_aligned_local;Username=exitpass;Password=change_me;Include Error Detail=true"
$env:ConnectionStrings__PosServer="Host=localhost;Port=5433;Database=posserver_api_smoke_validation_local;Username=exitpass;Password=change_me"
$env:FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow="false"
$env:FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow="false"
dotnet run --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-launch-profile
```

### Requester UI Profile

```powershell
cd D:\SourceCodes\ExitPass
$env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET="http://localhost:56065"
$env:VITE_OPERATOR_CONSOLE_USER_ID="77000000-0000-0000-0000-000000000010"
$env:VITE_OPERATOR_CONSOLE_SHIFT_ID="77000000-0000-0000-0000-000000000050"
$env:VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID="77000000-0000-0000-0000-000000000030"
$env:VITE_OPERATOR_CONSOLE_SITE_GROUP_ID="77000000-0000-0000-0000-000000000001"
$env:VITE_OPERATOR_CONSOLE_SITE_ID="77000000-0000-0000-0000-000000000002"
$env:VITE_OPERATOR_CONSOLE_PERMISSIONS="statutory-discounts.session.lookup,statutory-discounts.draft.view,statutory-discounts.draft.create,statutory-discounts.evidence.view,statutory-discounts.evidence.capture,statutory-discounts.policy.resolve,fiscal-issuance.status.read,ticket.lookup,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"
npm.cmd --prefix src\Services\OperatorConsoleUi run dev -- --host localhost --port 5175
```

### Reviewer/Apply UI Profile

```powershell
cd D:\SourceCodes\ExitPass
$env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET="http://localhost:56065"
$env:VITE_OPERATOR_CONSOLE_USER_ID="77000000-0000-0000-0000-000000000012"
$env:VITE_OPERATOR_CONSOLE_SHIFT_ID="77000000-0000-0000-0000-000000000052"
$env:VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID="77000000-0000-0000-0000-000000000030"
$env:VITE_OPERATOR_CONSOLE_SITE_GROUP_ID="77000000-0000-0000-0000-000000000001"
$env:VITE_OPERATOR_CONSOLE_SITE_ID="77000000-0000-0000-0000-000000000002"
$env:VITE_OPERATOR_CONSOLE_PERMISSIONS="statutory-discounts.draft.view,statutory-discounts.evidence.view,statutory-discounts.decision.review,statutory-discounts.decision.approve,statutory-discounts.decision.reject,statutory-discounts.payable-basis.apply,statutory-discounts.policy.resolve,fiscal-issuance.status.read,fiscal-issuance.void.command,operator-workflow-audit.view,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"
npm.cmd --prefix src\Services\OperatorConsoleUi run dev -- --host localhost --port 5175
```

## Manual Browser Steps For Darwin

1. Run the aligned preflight:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\operator-console\Invoke-StatutoryDiscountOperatorUatAlignedDbPreflight.ps1
```

2. Start Central PMS with the aligned DB command above.
3. Optionally verify the live API after Central PMS starts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\operator-console\Invoke-StatutoryDiscountOperatorUatAlignedDbPreflight.ps1 -SkipRebuild -SkipSeed -VerifyLiveApi
```

4. Start Operator Console UI with the requester profile.
5. Open `http://localhost:5175/operator-console/ticket-lookup`.
6. Search ticket `E2E-231-SESSION-001`.
7. Confirm the ticket resolves, current payable amount is `12500` / PHP 125.00, and Sales Invoice number is not shown as the ticket number.
8. Create the Senior Citizen statutory discount review draft.
9. Capture metadata-only evidence.
10. Attempt approval as the same requester and confirm it is denied safely.
11. Restart or run the UI with the reviewer/apply profile.
12. Open the draft/review context and approve as `uat-operations-supervisor`.
13. Apply payable basis.
14. Confirm application status `APPLIED`, non-null PayableBasisApplicationId, non-null applied tariff snapshot ID, and final payable `8929` / PHP 89.29.

## Safety Side-Effect Assertions

The API proof asserted no increase in rows for payment attempts, payment confirmations, fiscal issuance references, exit authorizations, gate authorization consumptions, coupon applications, provider outcomes, or reconciliation items.

No payment provider, HikCentral, gate, ExitAuthorization, refund/reversal, POS Server Sales Invoice, fiscal number allocation, final BIR rendering, or raw evidence byte path is used by this UAT readiness slice.

## Validation

Commands/results:

```text
scripts\operator-console\Invoke-StatutoryDiscountOperatorUatAlignedDbPreflight.ps1
Result: PASSED

dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountAlignedDbUatApiIntegrationTests" -m:1 /p:UseSharedCompilation=false
Result: PASSED, 1 test

dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationPersistenceIntegrationTests|FullyQualifiedName~OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests|FullyQualifiedName~ManagementPlatformIdentityRbacInventoryApiIntegrationTests" -m:1 /p:UseSharedCompilation=false
Result: PASSED, 26 tests

dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformUatIdentityRbacSeedTests|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests|FullyQualifiedName~CentralPmsRbacPolicyCatalogTests|FullyQualifiedName~StatutoryDiscount" -m:1 /p:UseSharedCompilation=false
Result: PASSED, 126 tests

dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore -m:1 /p:UseSharedCompilation=false
Result: PASSED
```

## Files Changed

- `scripts/operator-console/Invoke-StatutoryDiscountOperatorUatAlignedDbPreflight.ps1`
- `scripts/management-platform/Seed-ManagementPlatformUatIdentityRbac.sql`
- `scripts/management-platform/Verify-ManagementPlatformUatIdentityRbac.sql`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountAlignedDbUatApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/ManagementPlatformUatIdentityRbacSeedTests.cs`
- `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Statutory_Discount_Aligned_DB_UAT_Result_v1.0.md`

## Remaining Gap

The remaining browser ergonomics gap is an in-app local UAT identity switch. Backend RBAC and SoD are enforced and proven; this slice intentionally did not add a broad user-management UI or weaken backend authorization.
