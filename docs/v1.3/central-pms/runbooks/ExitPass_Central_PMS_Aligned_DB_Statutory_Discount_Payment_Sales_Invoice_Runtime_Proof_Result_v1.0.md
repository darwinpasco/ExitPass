# ExitPass Central PMS Aligned DB Statutory Discount Payment Sales Invoice Runtime Proof Result v1.0

## Result

PASSED.

The aligned canonical `exitpassdb_v1.2` generated SQL was used to rebuild a disposable Central PMS database, seed the v1.3 UAT identity/RBAC and statutory discount fixture, apply an approved Senior Citizen statutory discount to payable basis, create a payment attempt and confirmation for the discounted amount, and issue a local POS Server Sales Invoice through Central PMS live POS Server integration.

## Canonical DB Source

| Item | Value |
| --- | --- |
| DB repo generated SQL | `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` |
| DB repo validation SQL | `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql` |
| Central PMS disposable DB | `centralpms_aligned_discount_payment_si_runtime_local` |
| Central PMS connection | `Host=localhost;Port=5433;Database=centralpms_aligned_discount_payment_si_runtime_local;Username=exitpass;Password=change_me;Include Error Detail=true` |
| POS Server DB/runtime | `posserver_api_smoke_validation_local`, `http://localhost:5000` |

## Fixture

| Item | Value |
| --- | --- |
| Ticket/reference | `E2E-231-SESSION-001` |
| Entitlement type | `SENIOR_CITIZEN` |
| Requester/evidence actor | `uat-operator-support` |
| Reviewer/apply actor | `uat-operations-supervisor` |
| Runtime proof run id | `20260714133000` |
| Upstream finality reference | `STAT-DISCOUNT-CPS-LIVE-POS:20260714133000:SENIOR_CITIZEN:001` |

## Amount Proof

| Amount | Minor units | Display |
| --- | ---: | --- |
| Original gross | `12500` | PHP 125.00 |
| VAT-exclusive | `11161` | PHP 111.61 |
| VAT | `1339` | PHP 13.39 |
| Statutory discount | `2232` | PHP 22.32 |
| Final payable / tender | `8929` | PHP 89.29 |

## Runtime Chain Proof

| Step | Result |
| --- | --- |
| Statutory discount validation | Created and approved for `SENIOR_CITIZEN`; validation ID `4c4a1d77-44b7-4797-a5a7-74edb99819e0`. |
| Payable basis application | Applied successfully; PayableBasisApplicationId `db019618-cf18-48c2-aac0-afc323678cde`. |
| Applied tariff snapshot | `2baca9de-7627-4a61-a37a-f722c97fcbcf`. |
| Payment attempt | Created from the applied discounted tariff snapshot; payment attempt ID `cbb81d17-ee49-49b8-abfc-86ef8e5d6a09`; amount asserted as `8929`, not `12500`. |
| Payment confirmation | Recorded for the discounted amount; payment confirmation ID `6267cd8f-a2d7-4d16-8125-f67fc1bfed94`; amount asserted as `8929`. |
| Central PMS fiscal issuance reference | `4856b378-ef21-4f11-b7b0-3a4c9abc9161`; status API returned HTTP 200 and included recorded fiscal status. |
| POS Server Sales Invoice | Newly created through live local POS Server integration; POS fiscal document ID `fe33a258-ac25-4c81-b92b-50ecb274644f`; Sales Invoice number `SI-00000012-UAT`; fiscal sequence `12`. |
| POS Server readback | Direct readback succeeded; document ID, Sales Invoice number, fiscal sequence, and issued/created status matched Central PMS reference. |
| Operator Console fiscal status facade | Existing facade returned HTTP 200 and included the Sales Invoice number/status. |

The focused runtime test asserts the discounted fiscal payload facts: gross `12500`, VAT-exclusive `11161`, VAT `1339`, statutory discount `2232`, final payable/tender `8929`, Senior Citizen privilege context, payable-basis application traceability, applied tariff snapshot traceability, and payment attempt/confirmation context.

## Replay And Conflict Proof

| Proof | Result |
| --- | --- |
| First POS Server create | `NEWLY_CREATED`; POS document `fe33a258-ac25-4c81-b92b-50ecb274644f`; SI `SI-00000012-UAT`; sequence `12`. |
| Idempotent replay | `IDEMPOTENT_REPLAY`; same POS document ID, SI number, and fiscal sequence. |
| Conflict probe | Same idempotency/upstream finality reference with changed VAT amount failed closed with `fiscal_document_idempotency_conflict`; no Sales Invoice number returned for the conflict response. |
| Restored replay after conflict | `IDEMPOTENT_REPLAY`; same POS document ID, SI number, and fiscal sequence as first create. |

## Safety Assertions

- No live payment provider call was made.
- No live HikCentral call was made.
- No gate open or gate consumption was performed.
- No ExitAuthorization path was invoked by this proof.
- No refund or reversal was created.
- No POS Server source code was changed.
- Fiscal number allocation occurred only inside the allowed local POS Server runtime.
- No final BIR PDF/HTML/QR rendering path was invoked.
- No raw statutory evidence bytes or raw evidence payloads were stored or sent.
- The test compared unsafe side-effect record counts before and after the runtime proof.

## v1.3 Alignment Findings

The v1.3 docs inspected confirm the authority boundaries used by this proof:

- Central PMS owns payment-linked platform control state, payment finality, fiscal issuance reference recording, and approved statutory discount payable-basis update.
- POS Server remains the fiscal issuance authority for Sales Invoice creation, fiscal sequence, and fiscal readback.
- Operator Console remains non-payment and non-fiscal-authority; it may view fiscal status but must not issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.
- No v1.3 documentation conflict was found for this aligned-DB runtime proof.

## Validation Commands And Results

| Command | Result |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\central-pms\Invoke-AlignedDbStatutoryDiscountPaymentSalesInvoiceRuntimeProof.ps1 -RunId 20260714134000` | PASSED. Rebuilt Central PMS DB from canonical SQL, ran canonical DB validation, seeded/verifed UAT identity/RBAC, seeded/verified statutory pilot fixture, checked POS Server runtime, and ran live POS Server proof. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~LocalRuntime_WhenEnabled_IssuesDiscountedSalesInvoiceThroughCentralPmsLivePosServer" --logger "console;verbosity=detailed" -m:1 /p:UseSharedCompilation=false` | PASSED, 1/1. Captured runtime chain IDs above. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~PosServerFiscalDocumentRequestMapperTests\|FullyQualifiedName~FiscalIssuancePosServerLiveIntegrationServiceTests\|FullyQualifiedName~ManagementPlatformUatIdentityRbacSeedTests" -m:1 /p:UseSharedCompilation=false` | PASSED, 51/51. |
| `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore -m:1 /p:UseSharedCompilation=false` | PASSED. |

## Files Changed

- `scripts/central-pms/Invoke-AlignedDbStatutoryDiscountPaymentSalesInvoiceRuntimeProof.ps1`
- `scripts/management-platform/Seed-ManagementPlatformUatIdentityRbac.sql`
- `scripts/management-platform/Verify-ManagementPlatformUatIdentityRbac.sql`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/ManagementPlatformUatIdentityRbacSeedTests.cs`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Aligned_DB_Statutory_Discount_Payment_Sales_Invoice_Runtime_Proof_Result_v1.0.md`

## Remaining Gaps

None for this proof target.

Broader product work remains outside this slice: production service-to-service authentication hardening, Management Platform UI, and non-local POS Server deployment evidence.
