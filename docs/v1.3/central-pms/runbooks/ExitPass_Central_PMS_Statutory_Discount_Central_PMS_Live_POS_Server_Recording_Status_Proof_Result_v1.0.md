# ExitPass Central PMS Statutory Discount Central PMS Live POS Server Recording Status Proof Result v1.0

## Result

PASSED.

This records a local disposable runtime proof that Central PMS can submit a statutory-discounted Sales Invoice request to the local POS Server runtime, record the POS Server fiscal document result, read the Central PMS fiscal issuance status, read the Operator Console facade status, preserve idempotent replay, and fail closed on a semantic conflict.

Execution timestamp: 2026-07-11 23:19 Asia/Manila.

## v1.3 Document Check

Applicable ExitPass v1.3 documents under `docs/v1.3` were searched for statutory discount, payable basis, payment confirmation, Sales Invoice, POS Server, fiscal issuance, fiscal issuance reference, status read, Operator Console fiscal status, UAT, and safety-boundary terms.

| Document | Finding |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Supports Central PMS ownership of payable/payment orchestration and fiscal reference traceability. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Supports Central PMS integration boundaries and safe service-to-service behavior. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Supports statutory discount operator workflow, evidence posture, review, audit, and fiscal status visibility. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` | Supports applied payable basis, auditability, RBAC, and safe Operator Console read surfaces. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Constrains payment confirmation and finality integration; no HikCentral/gate behavior is part of this proof. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` | Supports payment attempt/confirmation separation and non-production proof boundaries. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Supports Sales Invoice/fiscal issuance and POS Server boundary ownership. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | Supports POS Server fiscal document persistence, fiscal number assignment, idempotency, readback, and discount privilege references. |
| `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md` | Constrains Central PMS request mapping, idempotency, semantic conflict, and safe POS Server response handling. |
| `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md` | Constrains POS Server fiscal document creation/read semantics. |
| `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Statutory_Discount_POS_Server_Runtime_Proof_Result_v1.0.md` | Prior slice proved direct POS Server boundary runtime creation/readback/replay/conflict for the discounted payload; this slice closes the Central PMS live-call recording/status gap. |

No v1.3 conflict was found. This proof remains inside v1.3 scope: no broad ordinance matrix, production policy activation, final BIR wording, POS Server source change, live payment provider, HikCentral, gate/ExitAuthorization, refund/reversal, or rendering/PDF/HTML/QR behavior was introduced.

## Proof Strategy

Proof strategy used: Central PMS opt-in integration smoke helper.

The smoke invokes `FiscalIssuancePosServerLiveIntegrationService` inside the Central PMS integration-test harness with:

- local Central PMS disposable DB: `exitpass_v12_dev`
- local POS Server disposable DB: `posserver_api_smoke_validation_local`
- local POS Server runtime: `http://localhost:5000`
- opt-in guard: `EXITPASS_RUN_STATUTORY_DISCOUNT_LIVE_POS_SMOKE=true`
- run id: `20260712004401`

The helper is disabled by default and now fails loudly if the integration DB is unavailable while the opt-in flag is set.

## Fixture

| Field | Value |
| --- | --- |
| Entitlement type | `SENIOR_CITIZEN` |
| Upstream finality reference | `STAT-DISCOUNT-CPS-LIVE-POS:20260712004401:SENIOR_CITIZEN:001` |
| Statutory discount validation ID | `1ed5157a-9ed4-46a4-94d6-4a0a4d8b3a16` |
| PayableBasisApplicationId | `a5b9eb04-bf45-45f2-b7c0-1b3719f43322` |
| Original tariff snapshot ID | `23100000-0000-0000-0000-000000000004` |
| Applied tariff snapshot ID | `f9b7581e-9cd2-4eee-a52b-ed58db5b830b` |
| Payment attempt ID | `9c3c39ff-a547-41aa-800b-a9268c54251d` |
| Payment confirmation/finality reference | `b7fd770b-41ce-4c90-b67d-ae4ead9aff1b` |
| Fiscal issuance reference ID | `b76a9afd-d69f-4187-a22f-11752e1f7e2c` |
| POS Server fiscal document ID | `eb94766c-c576-41af-90f3-64361155fa46` |
| Sales Invoice / fiscal document number | `SI-00000008-UAT` |
| Fiscal sequence value | `8` |

## Amount Proof

| Field | Minor units |
| --- | ---: |
| Original gross amount | `12500` |
| VAT-exclusive amount | `11161` |
| VAT amount | `1339` |
| Statutory discount amount | `2232` |
| Final payable amount | `8929` |
| Payment attempt amount | `8929` |
| Payment confirmation amount | `8929` |
| POS Server payable/tender amount | `8929` |

The Central PMS payable-basis application row persisted the same computation fields and `HALF_AWAY_FROM_ZERO` rounding mode.

## Runtime Results

| Step | Result |
| --- | --- |
| First Central PMS live-call | Passed; Central PMS called local POS Server and POS Server created `SI-00000008-UAT`. |
| Central PMS status read | Passed; `GET /v1/fiscal-issuance/references/b76a9afd-d69f-4187-a22f-11752e1f7e2c` returned `200` during the proof and exposed recorded fiscal status, assigned Sales Invoice number, POS Server read status, result classification, and evidence status. |
| POS Server readback | Passed; POS Server row exists for `eb94766c-c576-41af-90f3-64361155fa46`, Sales Invoice `SI-00000008-UAT`, sequence `8`, payable amount `8929`, entitlement `SENIOR_CITIZEN`, taxable amount `11161`, VAT amount `1339`, discount basis `11161`, discount amount `2232`, and VAT privilege amount `1339`. |
| Idempotent replay | Passed; exact replay returned POS Server `IDEMPOTENT_REPLAY` semantics with the same POS document, Sales Invoice number, and sequence. |
| Conflict/fail-closed | Passed; same idempotency key with changed tax amount returned `fiscal_document_idempotency_conflict` and did not allocate a new POS fiscal document or fiscal number. |
| Restored replay | Passed; exact replay after the conflict restored Central PMS final reference state to `FISCAL_ISSUANCE_REPLAYED` with `IDEMPOTENT_REPLAY`, assigned number, and semantic hash status `AVAILABLE`. |
| Operator Console facade | Passed; `GET /v1/ops/operator-console/fiscal-issuance/references/b76a9afd-d69f-4187-a22f-11752e1f7e2c` returned `200` during the proof and exposed the Sales Invoice status through the facade. |

## Final Central PMS Status

The final persisted Central PMS fiscal reference row shows:

- `fiscal_issuance_state`: `FISCAL_ISSUANCE_REPLAYED`
- `result_classification`: `IDEMPOTENT_REPLAY`
- `fiscal_issuance_evidence_status`: `FISCAL_DOCUMENT_NUMBER_ASSIGNED`
- `fiscal_number_assignment_state`: `ASSIGNED`
- `semantic_request_hash_status`: `AVAILABLE`
- `semantic_request_hash_source_fact_count`: `20`

## Safety Assertions

The proof asserted no increase in the local unsafe side-effect table counts for:

- payment confirmation outside the controlled discounted proof path
- payment provider call/mutation
- HikCentral call/write
- ExitAuthorization
- gate authorization/consumption/event
- refund/reversal
- POS Server replacement Sales Invoice
- rendering/PDF/HTML/QR/final BIR artifact

The runtime proof used local disposable/non-production databases only and did not modify POS Server source.

## Remaining Gaps

- This is a local runtime proof, not production certification.
- The Operator Console UI was not changed and no browser test was required for this slice.
- POS Server readback exposes the persisted statutory discount details through document context/detail rows; it still does not expose a single top-level payable amount field in the current read model.

## Validation

Runtime proof command:

```powershell
$env:EXITPASS_RUN_STATUTORY_DISCOUNT_LIVE_POS_SMOKE='true'
$env:EXITPASS_STATUTORY_DISCOUNT_LIVE_POS_SMOKE_RUN_ID='20260712004401'
$env:EXITPASS_STATUTORY_DISCOUNT_LIVE_POS_BASE_URL='http://localhost:5000'
Remove-Item Env:EXITPASS_TEST_MAIN_DB -ErrorAction SilentlyContinue
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~LocalRuntime_WhenEnabled_IssuesDiscountedSalesInvoiceThroughCentralPmsLivePosServer" --no-restore /m:1 /v:q
```

Result: passed, `1` test passed.

Focused validation:

| Command | Result |
| --- | --- |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~PosServerFiscalDocumentRequestMapperTests\|FullyQualifiedName~OperatorConsoleStatutoryDiscountComputationContractTests\|FullyQualifiedName~FiscalIssuancePosServerLiveIntegrationServiceTests" --no-restore /m:1 /v:q` | Passed, `58` tests. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountE2EIntegrationTests\|FullyQualifiedName~CreatePaymentAttemptPublicApiIntegrationTests\|FullyQualifiedName~RecordPaymentConfirmationIntegrationTests\|FullyQualifiedName~FiscalIssuanceStatusApiAccessPolicyIntegrationTests\|FullyQualifiedName~OperatorConsoleFiscalIssuanceStatusApiIntegrationTests" --no-restore /m:1 /v:q` | Passed, `39` tests. |
| `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore /m:1 /v:q` | Passed, `0` warnings, `0` errors. |
| `git diff --check` | Passed. |

## Files Changed

- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Statutory_Discount_Central_PMS_Live_POS_Server_Recording_Status_Proof_Result_v1.0.md`
