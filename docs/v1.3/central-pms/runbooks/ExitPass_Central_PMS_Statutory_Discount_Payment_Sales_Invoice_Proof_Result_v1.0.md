# ExitPass Central PMS Statutory Discount Payment Sales Invoice Proof Result v1.0

## Result

PASSED - integration payload proof.

This proof verifies the controlled local Central PMS path:

approved statutory discount -> applied payable basis -> payment attempt -> payment confirmation -> fiscal issuance reference -> POS Server Sales Invoice request payload mapping.

No live POS Server fiscal document was created by this slice.

## v1.3 Source Check

Applicable ExitPass v1.3 documents were searched under `docs/v1.3` and inspected for statutory discount, payable-basis, payment, Sales Invoice, POS Server, fiscal issuance, audit, and UAT constraints.

| Document | Finding |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Central PMS owns payment-linked control state, payable basis, payment finality, fiscal issuance reference recording, and ExitAuthorization; POS Server owns Sales Invoice issuance. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Central PMS owns payable-basis effect after approved discount validation; normal sequence is payment finality, POS fiscal issuance, Central PMS fiscal reference recording, then ExitAuthorization. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console supports statutory discount review/governance but must not collect payment, declare payment finality, issue Sales Invoices, issue ExitAuthorization, or open gates. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` | Operator Console statutory-discount workflow is governance/evidence/audit oriented and delegates payable-basis/payment/fiscal authority to Central PMS/POS Server boundaries. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Assisted terminal may capture statutory discount inputs, but Central PMS/Discount workflow owns policy resolution and payable-basis update before payment. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` | Statutory discount capture must refresh payable basis before payment flow; terminal does not independently approve entitlement or mutate payment finality. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Sales Invoice issuance belongs to the resolved Site POS Server after approved payment/fiscal facts; POS/Invoicing must preserve discount/fiscal attribution boundaries. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | POS Server is fiscal authority only and must not approve statutory entitlement, mutate Central PMS payable basis, declare payment finality, or issue ExitAuthorization. |
| `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md` | POS Server request model supports payable basis, discount references, tax details, tender details, discount privilege details, totals, and safe reference context. |
| `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Statutory_Discounts_Current_State_Audit_v1.0.md` | Identified the need to prove payable/payment/Sales Invoice handoff after computation and payable-basis application persistence alignment. |

No v1.3 conflict was found. The proof stays within v1.3 scope and does not introduce broad local ordinance logic, final BIR statutory wording, production policy activation, POS Server source changes, live payment provider calls, HikCentral calls, gate/ExitAuthorization behavior, refund/reversal behavior, or rendering/PDF/HTML/QR behavior.

## Fixture

| Field | Value |
| --- | --- |
| Entitlement type | `SENIOR_CITIZEN` |
| Parking session | `23100000-0000-0000-0000-000000000003` |
| Original tariff snapshot | `23100000-0000-0000-0000-000000000004` |
| Policy code | `PH_ATC_SENIOR_CITIZEN_SITE_POLICY_231` |
| Evidence mode | Metadata-only operator-confirmed Senior Citizen ID evidence |
| Payment provider code | `GCASH` |
| POS Server target | Request payload mapping only; no live POS Server call |

PWD coverage is included through focused POS Server request mapper tests using the same VAT-exclusive/20% statutory computation payload shape.

## Amount Proof

| Amount | Minor units | Major units |
| --- | ---: | ---: |
| Original gross | `12500` | PHP 125.00 |
| VAT-exclusive amount | `11161` | PHP 111.61 |
| VAT amount | `1339` | PHP 13.39 |
| Statutory discount amount | `2232` | PHP 22.32 |
| Final payable | `8929` | PHP 89.29 |
| Payment attempt amount | `8929` | PHP 89.29 |
| Payment confirmation amount | `8929` | PHP 89.29 |

The payment attempt was created from the applied tariff snapshot, not the original gross tariff snapshot. Payment confirmation accepted only the discounted amount and currency.

## Traceability

The proof asserts traceability across:

- statutory discount validation ID;
- non-null `PayableBasisApplicationId`;
- original tariff snapshot ID;
- applied tariff snapshot ID;
- payment attempt ID;
- payment confirmation ID;
- fiscal issuance reference ID;
- POS Server Sales Invoice request `payableBasisRef`;
- POS Server request discount reference and discount privilege detail context.

## Sales Invoice Payload Assertions

The mapped POS Server Sales Invoice request includes:

- payable amount `8929`;
- tender amount `8929`;
- document line gross `12500`, discount `2232`, tax `1339`, net `8929`;
- tax detail taxable amount `11161`, tax amount `1339`, tax rate `12`;
- approved discount reference with `appliesStatutoryDiscountTreatment = true`;
- `PayableBasisApplicationId` in safe reference context;
- entitlement type `SENIOR_CITIZEN` in safe reference context;
- discount privilege basis `11161`, discount `2232`, VAT privilege amount `1339`;
- semantic request hash source status `AVAILABLE`.

The request does not include raw evidence payloads, raw callback payloads, raw evidence images, raw ID image bytes, provider secrets, or the captured Senior Citizen reference number.

## Safety Assertions

This proof did not:

- call a live payment provider;
- call live HikCentral;
- call POS Server runtime;
- create a POS Server Sales Invoice;
- allocate a fiscal number;
- issue ExitAuthorization;
- trigger gate behavior;
- create refund/reversal;
- render PDF/HTML/QR/final BIR artifact;
- store raw statutory evidence payloads.

## Remaining Gaps

- This is an integration payload proof, not a live POS Server runtime proof.
- POS Server runtime proof remains needed for discounted Sales Invoice creation once a disposable non-production POS Server fixture is approved.
- Final BIR statutory wording/rendering remains out of scope.
- Broader local ordinance matrices remain out of scope.

## Validation

| Command | Result |
| --- | --- |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName=ExitPass.CentralPms.IntegrationTests.Api.OperatorConsoleStatutoryDiscountE2EIntegrationTests.EndToEnd_WhenOperatorCompletesRequiredEvidenceFlow_AppliesApprovedPayableBasis" --no-restore /m:1` | Passed, 1 test |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~PosServerFiscalDocumentRequestMapperTests" --no-restore /m:1` | Passed, 11 tests |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountE2EIntegrationTests\|FullyQualifiedName~RecordPaymentConfirmationIntegrationTests" --no-restore /m:1` | Passed, 9 tests |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~CreatePaymentAttemptPublicApiIntegrationTests" --no-restore /m:1` | Passed, 3 tests |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~PosServerFiscalDocumentRequestMapperTests\|FullyQualifiedName~OperatorConsoleStatutoryDiscountComputationContractTests" --no-restore /m:1` | Passed, 25 tests |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountE2EIntegrationTests\|FullyQualifiedName~RecordPaymentConfirmationIntegrationTests\|FullyQualifiedName~CreatePaymentAttemptPublicApiIntegrationTests" --no-restore /m:1` | Passed, 12 tests |
| `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore /m:1` | Passed, 0 warnings, 0 errors |
| `git diff --check` | Passed; Git reported line-ending normalization warnings only |
