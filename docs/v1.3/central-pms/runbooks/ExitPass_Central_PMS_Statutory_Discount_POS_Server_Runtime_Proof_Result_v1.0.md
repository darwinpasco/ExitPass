# ExitPass Central PMS Statutory Discount POS Server Runtime Proof Result v1.0

## Result

PASSED.

This record proves disposable local POS Server runtime creation, readback, idempotent replay, and conflict/fail-closed behavior for a statutory-discounted Sales Invoice payload derived from the approved Central PMS statutory discount computation contract.

## Scope

| Item | Value |
| --- | --- |
| Proof strategy | Direct POS Server boundary runtime proof |
| POS Server URL | `http://localhost:5000` |
| POS Server process | Existing local `dotnet run --project src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --no-launch-profile` |
| Central PMS live-call recording | Not performed in this slice |
| POS Server source changes | None |
| Entitlement type | `SENIOR_CITIZEN` |
| Upstream finality ref / idempotency key | `STAT-DISCOUNT-POS-RUNTIME:20260711222243:SENIOR_CITIZEN:newly_created:001` |

## v1.3 Source Check

Applicable v1.3 documents were searched and inspected for statutory discount, payable basis, payment confirmation, Sales Invoice, POS Server, fiscal issuance, discount privilege, UAT, and safety-boundary constraints.

Key findings:

| Document | Finding |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Preserves Central PMS/payment-finality authority and POS Server Sales Invoice authority. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Requires fiscal issuance before normal exit authorization and keeps gate/exit outside fiscal proof scope. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console statutory discounts remain evidence/review/payable-basis workflow, not payment provider or fiscal authority. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` | Supports statutory discount traceability and audit posture before payment/fiscal proof. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` and system design docs | Preserve terminal/channel boundary; terminals do not issue Sales Invoices or ExitAuthorization. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | POS Server owns Sales Invoice/fiscal issuance and fiscal numbering. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | POS Server runtime persists fiscal document shell, fiscal number assignment, idempotency, discount privilege references, and readback. |
| `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md` | Supports Central PMS to POS Server fiscal issuance request/response contract and fail-closed conflict posture. |

No v1.3 conflict was found. The proof stayed inside the v1.3 Central PMS/POS Server authority split.

## Fixture

| Field | Value |
| --- | --- |
| POS Server fiscal document ID | `d26c6590-7416-45dd-aa3b-b6a6b995f2c3` |
| Sales Invoice number | `SI-00000003-UAT` |
| Fiscal sequence value | `3` |
| Fiscal series | `central-pms-uat-si-sequence-policy` |
| Site POS Server ID | `10000000-0000-4000-8000-000000000201` |
| Site POS Server ref | `DEV-POS-SERVER-ATC-001` |
| Fiscal document type code ID | `10000000-0000-4000-8000-000000000103` |
| Fiscal document status code ID | `10000000-0000-4000-8000-000000000107` |
| Statutory discount validation ID | `23100000-0000-0000-0000-000000000901` |
| PayableBasisApplicationId | `23100000-0000-0000-0000-000000000902` |
| Applied tariff snapshot ID | `23100000-0000-0000-0000-000000000903` |
| Payment attempt ID | `23100000-0000-0000-0000-000000000904` |
| Payment confirmation ID | `23100000-0000-0000-0000-000000000905` |

## Amount Proof

| Amount | Minor units |
| --- | ---: |
| Original gross | `12500` |
| VAT-exclusive amount | `11161` |
| VAT amount | `1339` |
| Statutory discount | `2232` |
| Final payable | `8929` |
| Payment attempt amount represented | `8929` |
| Payment confirmation amount represented | `8929` |

Runtime-compatible POS Server line representation:

| Field | Minor units |
| --- | ---: |
| Line gross | `11161` |
| Line discount | `2232` |
| Line tax | `0` |
| Line net | `8929` |
| Tax detail taxable amount | `11161` |
| Tax detail tax amount | `1339` |
| Discount privilege basis | `11161` |
| Discount privilege amount | `2232` |
| VAT privilege amount | `1339` |

The original gross amount `12500`, VAT amount `1339`, entitlement type, policy, and payable-basis application reference were carried as safe reference/context metadata.

## Runtime Results

| Step | Result |
| --- | --- |
| First create | HTTP `202`; `succeeded=true`; `code=accepted`; `resultClassification=newly_created`; Sales Invoice `SI-00000003-UAT`; sequence `3` |
| Readback | HTTP `200`; `code=found`; document, line, tender, tax detail, discount privilege detail, and total persisted |
| Idempotent replay | HTTP `202`; `resultClassification=idempotent_replay`; same fiscal document ID, Sales Invoice number, and sequence |
| Conflict/fail-closed | HTTP `409`; `code=fiscal_document_idempotency_conflict`; `errorPosture=do_not_retry_without_request_change`; no fiscal number assigned in response |
| Final readback after conflict | HTTP `200`; still `SI-00000003-UAT`; sequence still `3`; semantic request hash status `matched` |

## Readback Assertions

Observed through `GET /v1/fiscal-documents/d26c6590-7416-45dd-aa3b-b6a6b995f2c3`:

- fiscal document exists;
- Sales Invoice number is `SI-00000003-UAT`;
- fiscal sequence value is `3`;
- one fiscal line, one tender, one tax detail, one discount privilege detail, and one total are present;
- tender amount is `8929`;
- tax detail amount is `1339`;
- discount privilege amount is `2232`;
- VAT privilege amount is `1339`;
- evidence reference is metadata-only: `metadata-only-evidence-captured`;
- approval reference is the statutory discount validation ID;
- semantic request hash status is `matched`.

## Safety Assertions

This proof did not:

- call a live payment provider;
- call live HikCentral;
- open gate;
- issue ExitAuthorization;
- create refund/reversal;
- render PDF/HTML/QR/final BIR artifact;
- store raw statutory evidence payloads;
- modify POS Server source;
- use production/shared/customer data.

## Remaining Gaps

- This slice is a direct POS Server boundary runtime proof. Central PMS live-call recording/status update for this discounted fixture was not performed.
- POS Server readback exposes the persisted line, tax, tender, discount privilege, and total rows. It does not expose a top-level payable amount field in the current read model.
- The first two attempted runtime submissions were safely rejected before persistence while aligning the line representation to POS Server validation.

## Validation

Runtime commands/results:

- `GET http://localhost:5000/v1/fiscal-documents/00000000-0000-0000-0000-000000000000`: HTTP `404`, confirming POS Server route availability.
- `POST http://localhost:5000/v1/fiscal-documents/`: HTTP `202`, `newly_created`.
- `GET http://localhost:5000/v1/fiscal-documents/d26c6590-7416-45dd-aa3b-b6a6b995f2c3`: HTTP `200`, `found`.
- repeated `POST http://localhost:5000/v1/fiscal-documents/` with same semantic request: HTTP `202`, `idempotent_replay`.
- repeated `POST http://localhost:5000/v1/fiscal-documents/` with same idempotency key and changed tax detail: HTTP `409`, `fiscal_document_idempotency_conflict`.

Focused repository validation:

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~PosServerFiscalDocumentRequestMapperTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountComputationContractTests" --no-restore /m:1 /v:q`: passed, 25 passed.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountE2EIntegrationTests|FullyQualifiedName~RecordPaymentConfirmationIntegrationTests|FullyQualifiedName~CreatePaymentAttemptPublicApiIntegrationTests" --no-restore /m:1 /v:q`: passed, 12 passed.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore /m:1 /v:q`: passed with existing nullable/XML documentation warnings.
- `git diff --check`: passed.
