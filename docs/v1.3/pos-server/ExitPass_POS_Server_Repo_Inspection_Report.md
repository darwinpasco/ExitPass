# ExitPass POS Server Repository Inspection Report

## 1. Purpose

This report summarizes a read-only inspection of the current `ExitPass-PoSServer` repository and compares observed implementation reality against the approved ExitPass v1.3 POS Server documentation baseline.

This is a documentation reality-check only. It does not modify POS Server implementation, database schema, API contracts, tests, scripts, or source files.

## 2. Repositories Inspected

| Repository | Path | Role |
| --- | --- | --- |
| ExitPass-PoSServer | `D:\SourceCodes\ExitPass-PoSServer` | Primary implementation repository inspected read-only. |
| ExitPass | `D:\SourceCodes\ExitPass` | Documentation/reference repository where this report is created. |

## 3. Branches Inspected

| Repository | Branch |
| --- | --- |
| ExitPass-PoSServer | `runtime/fiscal-numbering-idempotency-design` |
| ExitPass | `docs/v1.3-pos-server-repo-inspection` |

Recent POS Server repository history shows fiscal numbering work was recently merged or documented:

- `9f782b1` Merge pull request `#64` from `runtime/fiscal-numbering-allocation-design`
- `9459175` docs: design fiscal number allocation
- `98eae30` Merge pull request `#63` from `runtime/fiscal-numbering-read-model`
- `49d4003` feat: expose fiscal numbering read model
- `9817232` Merge pull request `#62` from `db/fiscal-numbering-columns`
- `0466166` feat: add fiscal numbering columns

## 4. Inspection Method

Inspection was performed with read-only repository commands:

- checked git branch/status in both repositories
- listed solution/project structure
- listed source, test, database, documentation, and workflow files
- inspected API endpoint routing and request/response mapping
- inspected runtime fiscal document creation/read services
- inspected PostgreSQL persistence repository and reader
- inspected database state SQL object inventory and apply-order manifest
- inspected validation/rebuild/drift script documentation and CI workflow
- inspected tests for behavior assertions and authority-boundary checks
- searched for authority-risk terms such as `ExitAuthorization`, gate/open-gate behavior, POS-owned payment finality, idempotency, sequence allocation, reports, reprints, and adjustments

No tests or scripts were executed because this task is inspection-only and test/script execution may generate local artifacts.

## 5. POS Server Repository Structure Summary

Observed top-level structure:

| Area | Observed files/folders | Assessment |
| --- | --- | --- |
| Solution | `ExitPass.PoSServer.sln` | Implemented |
| API project | `src/ExitPass.PosServer.Api` | Implemented |
| Runtime project | `src/ExitPass.PosServer.Runtime` | Implemented |
| PostgreSQL persistence project | `src/ExitPass.PosServer.Persistence.Postgres` | Implemented |
| Tests | `tests/ExitPass.PosServer.*` | Implemented |
| Database state | `db/state`, `db/rebuild`, `db/validation`, `db/reference-data`, `db/scripts` | Partially implemented |
| Documentation | `docs/v1.3/...` | Implemented as repository-local documentation copy/baseline |
| CI | `.github/workflows/pos-db-validation.yml` | Implemented for DB static and controlled-code load validation |

Observed counts:

- Source C# files: `48`
- Test C# files: `19`
- Database table SQL files under `db/state/tables`: `51`
- Controlled-code source family JSON files: `19`

## 6. Current API Implementation Summary

Implemented API routing is limited to fiscal document create/read endpoints:

| Endpoint | Source | Assessment |
| --- | --- | --- |
| `POST /v1/fiscal-documents/` | `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentEndpointRouteBuilderExtensions.cs` | Implemented |
| `GET /v1/fiscal-documents/{fiscalDocumentId:guid}` | `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentEndpointRouteBuilderExtensions.cs` | Implemented |

Observed request/response areas:

- `CreateFiscalDocumentRequest` supports Site POS Server context, fiscal document type/status, business day, Central PMS parking/payment references, `PaymentFinalityRef`, `VendorAckRef`, document links, fiscal lines, tenders, tax details, discount privilege details, totals, and reference context.
- `GetFiscalDocumentResponse` returns a `FiscalDocumentReadModel`.
- Creation endpoint returns `202 Accepted` with a fiscal document id on successful persistence.
- Read endpoint returns `200 OK` for found documents and `404` for missing documents.
- Persistence-not-configured and invalid-configuration paths fail closed with `503`.

Not found:

- Runtime endpoints for X-read/Z-read generation.
- Runtime endpoints for BIR Sales Summary or Annex E generation.
- Runtime endpoints for Electronic Journal generation.
- Runtime endpoints for POSLog/export generation.
- Runtime endpoints for Sales Invoice rendering or digital SI URL creation.
- Runtime endpoints for reprint request handling.
- Runtime endpoints for void/refund/cancel/return adjustment handling.
- Runtime endpoints for fiscal reset/recovery.
- Runtime endpoints for fiscal number allocation.

## 7. Current Database / State SQL Implementation Summary

Implemented as repository SQL state:

- `db/state/schemas/pos.sql`
- 51 table SQL files under `db/state/tables`
- deterministic apply-order manifest at `db/rebuild/pos_sql_apply_order.txt`
- expected inventory at `db/validation/pos_expected_inventory.json`
- prohibited-pattern rules at `db/validation/pos_prohibited_patterns.json`
- controlled-code source JSON and generated SQL under `db/reference-data/controlled-codes`

Major database object areas present as SQL state:

- Site POS Server and fiscal identity.
- Channel terminal registry and status/capabilities.
- Fiscal document header, status history, links, lines, tenders, tax details, discount privilege details, and totals.
- Fiscal sequence policies/states/gap audit.
- Fiscal counter states, fiscal state snapshots, lock states, recovery requests, continuity check results, and anchor refs.
- Idempotency records, retry records, and fiscal operation exceptions.
- Digital SI URLs and access events.
- Reprint requests and reprint output refs.
- Fiscal adjustments and adjustment status history.
- Fiscal report requests/scopes, X/Z reports, BIR Sales Summary, Annex E reports, and report output refs.
- Electronic Journal records.
- Fiscal export requests/packages/items, export schema profile refs, and validation results.
- Fiscal, privileged, configuration, and access audit tables.

Important reality distinction:

- These SQL objects exist as database state artifacts.
- Many comments explicitly frame objects as posture/reference/evidence and not completed runtime behavior.
- Database functions, triggers, PostgreSQL sequence objects, extensions, outbox/event publication tables, POS-owned payment finality objects, POS-owned ExitAuthorization objects, and gate execution tables are prohibited by validation configuration.

## 8. Current Validation / Rebuild / Drift Tooling Summary

Implemented tooling:

- `db/scripts/Invoke-PosDbChecks.ps1`
- `db/scripts/README.md`
- `.github/workflows/pos-db-validation.yml`

Supported script modes documented:

- `Static`
- `Rebuild`
- `Inventory`
- `Drift`
- `ControlledCodeLoad`
- `All`

Docker-backed validation is documented and supported through `-UseDockerPsql`.

CI currently runs:

- `Static` validation
- `ControlledCodeLoad` validation against a CI-local PostgreSQL 16 service database
- evidence upload as `pos-db-validation-evidence`

Limitations documented by the repository:

- CI does not yet run full rebuild, inventory, drift, column-level, constraint-level, or deeper database validation.
- Drift checks report only and do not update repository SQL.
- Docker mode requires Docker and an available/pullable PostgreSQL client image.

## 9. Current Test Coverage Summary

Observed test projects:

- `tests/ExitPass.PosServer.Runtime.Tests`
- `tests/ExitPass.PosServer.Api.Tests`
- `tests/ExitPass.PosServer.Persistence.Postgres.Tests`
- `tests/ExitPass.PosServer.Api.IntegrationTests`

Implemented test coverage includes:

- runtime fiscal document creation validation
- runtime fiscal document read behavior
- API creation endpoint mapping and validation failures
- API read endpoint mapping and failure modes
- fail-closed behavior when persistence is not configured or invalid
- PostgreSQL repository SQL boundary checks
- PostgreSQL reader SQL boundary checks
- integration smoke test for POST fiscal document creation and GET readback
- integration rollback behavior on late persistence failure
- fiscal numbering read-model exposure
- checks that request/response types do not expose payment finality/gate/ExitAuthorization behavior
- checks that repository SQL does not target report/export/gate/exit areas from the create/read repository paths

Tests were inspected but not executed.

## 10. Current Fiscal Document Issuance Behavior

Implemented:

- `POST /v1/fiscal-documents/` accepts a fiscal document creation request.
- Runtime service validates:
  - Site POS Server reference and ids.
  - fiscal document type/status ids.
  - approved payable-basis reference.
  - upstream finality reference.
  - approved statutory discount references when statutory discount treatment is applied.
  - fiscal line validity and unique line sequence.
  - tender validity and currency match.
  - tax detail validity.
  - discount privilege detail validity.
  - fiscal total validity.
  - absence of raw sensitive evidence, raw payment payload, tokens, secrets, card numbers, CVV, or provider callback payload markers.
- PostgreSQL repository writes fiscal document header, status history, links, lines, tenders, tax details, discount privilege details, and totals inside a database transaction.
- Late persistence failure rolls back the fiscal document shell in integration tests.

Partially implemented:

- The POST path creates/persists a fiscal document shell and associated child records.
- It does not yet complete full fiscal issuance in the v1.3 sense because runtime fiscal number allocation, Sales Invoice number assignment, digital SI URL creation, rendered SI output, EJ/POSLog generation, and fiscal report integration are not implemented in the POST path.

## 11. Current Fiscal Numbering and Idempotency Behavior

Implemented:

- Database columns for fiscal numbering exist in `pos.fiscal_documents`.
- Read model exposes nullable fiscal numbering fields:
  - `FiscalIdentityId`
  - `FiscalSequencePolicyId`
  - `FiscalSequenceValue`
  - `FiscalDocumentNumber`
  - `FiscalSeries`
  - `FiscalNumberPrefixText`
  - `FiscalNumberSuffixText`
  - `FiscalNumberAssignedAt`
  - `FiscalNumberAssignedByRef`
- `GET /v1/fiscal-documents/{id}` can return populated fiscal numbering fields if they already exist in the database.
- SQL state includes fiscal sequence policy/state/gap audit tables.
- SQL state includes idempotency records.

Not implemented in runtime:

- POST fiscal document creation does not allocate fiscal numbers.
- POST insert SQL explicitly does not insert `fiscal_sequence_policy_id`, `fiscal_sequence_value`, or `fiscal_document_number`.
- Tests assert the create repository SQL does not contain `fiscal_sequence_policy_id`, `fiscal_sequence_value`, `fiscal_document_number`, or `fiscal_sequence_states`.
- No observed runtime fiscal number allocator locks sequence state with `FOR UPDATE`.
- No observed runtime semantic request hash computation.
- No observed runtime idempotency key handling in the POST request or service path.
- No observed runtime behavior that returns a fiscal number only after durable allocation/commit.

Assessment:

- Fiscal numbering is partially implemented as database/read-model support.
- Runtime fiscal number allocation and safe idempotent issuance remain not implemented.
- Idempotency is represented in SQL posture but not wired into current runtime fiscal document creation behavior.

## 12. Current Reporting / Export / Reprint / Adjustment Behavior

Implemented as SQL state only:

- X/Z report tables.
- BIR Sales Summary table.
- Annex E report table.
- Electronic Journal records table.
- Export request/package/item/validation tables.
- Reprint request/output-ref tables.
- Fiscal adjustment and adjustment status history tables.
- Digital SI URL and access event tables.

Not found in runtime code:

- X-read generation service.
- Z-read generation service.
- BIR Sales Summary generation service.
- Annex E generation service.
- Electronic Journal writer/generator.
- POSLog or ARTS export generator.
- Reprint handling endpoint/service.
- Adjustment document runtime service.
- Digital Sales Invoice URL creation service.
- QR presentation support beyond documentation/API design posture.

Assessment:

- Reporting/export/reprint/adjustment behavior is documented and modeled in SQL state.
- Runtime behavior is not implemented beyond fiscal document shell create/read.

## 13. Runtime Sales Invoice Readiness Assessment

Current readiness: partially implemented, not ready for production Sales Invoice issuance.

Ready or partially ready:

- API entrypoint for fiscal document creation exists.
- Runtime validates required upstream payable-basis and finality references.
- Runtime rejects unapproved statutory discount treatment.
- Runtime rejects raw sensitive evidence/payment payload markers.
- Runtime persists a fiscal document shell and child fiscal facts transactionally.
- Runtime GET can read fiscal document details and nullable fiscal numbering fields.

Not ready:

- No runtime fiscal number allocation.
- No runtime SI number assignment in POST.
- No runtime idempotency enforcement.
- No durable allocation-and-document-creation transaction covering sequence state and fiscal document creation.
- No digital SI URL creation in runtime.
- No rendered Sales Invoice output.
- No EJ/POSLog runtime generation.
- No X/Z/BIR Sales Summary runtime reporting.
- No fiscal reference callback/return choreography to Central PMS beyond returning an accepted response with fiscal document id.

Safest current interpretation:

- The repository currently implements a fiscal document persistence shell and read model foundation.
- It does not yet implement complete v1.3 runtime Sales Invoice issuance.

## 14. Authority Boundary Check

| Boundary | Repository finding | Assessment |
| --- | --- | --- |
| POS Server is fiscal issuance authority only | README, DB comments, tests, and source design preserve this boundary. | Preserved |
| POS Server does not declare payment finality | Code stores `PaymentFinalityRef` as reference; tests check no payment finality/gate behavior is exposed. | Preserved |
| POS Server does not issue ExitAuthorization | No runtime endpoint/class found; prohibited-pattern checks include POS-owned ExitAuthorization. | Preserved |
| POS Server does not open gates | No runtime gate/open-gate behavior found; tests and validation prohibit gate tables/fields. | Preserved |
| POS Server does not approve statutory entitlement | Runtime requires approved discount reference; request DTOs tested not to expose entitlement approval fields. | Preserved |
| POS Server does not mutate Central PMS payable basis directly | Runtime requires upstream payable-basis ref and stores/references it; no Central PMS mutation path found. | Preserved |
| POS Server does not activate continuity | Continuity objects are SQL evidence posture; no activation runtime found. | Preserved |
| POS Server does not approve manual release | No manual-release approval runtime found. | Preserved |
| Central PMS owns payment finality, fiscal reference recording, degraded resolve, and ExitAuthorization | Repository stores Central PMS references only; no Central PMS authority objects found. | Preserved by absence |
| POS Server issues fiscal documents and returns fiscal identity/status to Central PMS | Partially present: POST returns fiscal document id/status, but full SI identity/numbering is not yet allocated. | Partially implemented |

No v1.3 authority violations were found in the inspected runtime code.

## 15. Gaps Versus POS Server System Design v1.0

Major gaps:

| Design area | Current reality | Gap |
| --- | --- | --- |
| Runtime SI issuance | Fiscal document shell create/read exists. | Complete SI issuance not implemented. |
| Fiscal number allocation | SQL/read-model support exists. | Runtime allocation missing. |
| Idempotency | SQL posture exists. | Runtime idempotency missing. |
| Durable number allocation transaction | Fiscal document persistence transaction exists. | No sequence-state allocation within same transaction. |
| Digital SI URL | SQL state exists. | Runtime service/endpoint missing. |
| Sales Invoice rendering | Documented only. | Runtime renderer missing. |
| X-read/Z-read | SQL state exists. | Runtime generation missing. |
| BIR Sales Summary / Annex E | SQL state exists. | Runtime generation missing. |
| EJ/POSLog | SQL state exists. | Runtime generation/export missing. |
| Reprints | SQL state exists. | Runtime request/output behavior missing. |
| Adjustments | SQL state exists. | Runtime adjustment workflow missing. |
| Reset counter / Z-counter / GTA | SQL state exists. | Runtime update rules and counter integrity logic missing. |
| Fiscal exception workflow | SQL state and docs exist. | Runtime retry/exception workflow missing. |
| Central PMS fiscal reference recording handoff | API returns fiscal document id/status. | Full choreography with Central PMS remains API/design integration work. |

## 16. Documentation-Only vs Implemented Reality

Implemented:

- API project and minimal API routing.
- Fiscal document POST and GET endpoints.
- Runtime fiscal document validation.
- PostgreSQL persistence for fiscal document shell and child fiscal facts.
- PostgreSQL read model for fiscal document shell/child rows and nullable fiscal numbering fields.
- Database state SQL artifacts for the broader POS Server model.
- Validation/rebuild/drift script package.
- CI workflow for static and controlled-code load validation.
- Tests for fiscal document creation/read, persistence SQL boundaries, fail-closed behavior, and authority boundaries.

Partially implemented:

- Fiscal numbering: DB/read model present; allocation missing.
- Idempotency: DB model present; runtime behavior missing.
- Fiscal reporting/export/reprint/adjustment: SQL state present; runtime behavior missing.

Documented only or not found:

- full Sales Invoice rendering and issue output
- digital SI URL runtime generation
- QR presentation runtime integration
- X/Z report generation
- BIR Sales Summary / Annex E generation
- EJ/POSLog generation/export
- reprint execution
- fiscal adjustment execution
- reset/Z-counter/GTA runtime update mechanics
- supervised recovery automation
- Central PMS integration callback/reference recording beyond POST response

## 17. Risks Found

| Risk | Assessment |
| --- | --- |
| Runtime fiscal number allocation not implemented | Major blocker for safe v1.3 Sales Invoice issuance. |
| Idempotency not wired into POST fiscal document creation | Major blocker for safe retry under fiscal issuance timeout/failure. |
| POST returns `accepted` and fiscal document id without fiscal document number | Risk if external consumers misinterpret accepted persistence shell as fully issued SI. |
| SQL state contains many report/export/recovery objects without runtime behavior | Risk of overestimating implementation completeness from schema presence alone. |
| README still says current content is documentation baseline/future implementation | Minor documentation staleness, because source/API/tests now exist. |
| Legacy terms in DB comments such as `cashier POS` and `EC/continuity` | Terminology cleanup risk in SQL comments/documentation; not a runtime authority issue. |
| CI currently validates static and controlled-code load only | Deeper rebuild/inventory/drift/constraint validation is not yet CI-enforced. |

## 18. Recommended Next Work Items

Recommended sequence:

1. Implement runtime fiscal number allocation and POST idempotency as the next safest work item.
2. Define and enforce semantic request hash/idempotency key behavior for fiscal issuance.
3. Allocate fiscal number in the same durable transaction as fiscal document creation, or explicitly redesign POST so shell creation cannot be mistaken for fiscal issuance.
4. Update response semantics so consumers can distinguish persisted shell, numbered Sales Invoice, and fully issued fiscal document states.
5. Add tests for duplicate POST, retry after timeout, conflicting semantic hash, allocation rollback, sequence gap policy, and durable commit behavior.
6. Add runtime support for digital SI URL only after safe SI number allocation exists.
7. Defer X/Z/BIR Sales Summary/EJ/POSLog/reprints/adjustments until issuance and idempotency are safe.

## 19. Suggested Next Codex Task

Suggested task:

> Draft the POS Server runtime fiscal numbering and idempotent Sales Invoice issuance implementation plan, based on the approved v1.3 POS Server System Design and this repository inspection. The task should inspect the existing POST fiscal document creation path, sequence state SQL, idempotency_records SQL, tests, and fiscal numbering design docs, then produce a scoped implementation plan without modifying code.

If implementation is desired immediately after planning, a safe implementation task should be limited to:

- runtime fiscal number allocation
- POST idempotency
- semantic request hash policy
- transaction and rollback behavior
- tests proving no duplicate fiscal numbers and no unsafe replay

## 20. Appendix A: Notable Files Inspected

ExitPass-PoSServer:

- `ExitPass.PoSServer.sln`
- `README.md`
- `.github/workflows/pos-db-validation.yml`
- `src/ExitPass.PosServer.Api/Program.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentEndpointRouteBuilderExtensions.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentCreationEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentReadEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentRequest.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/GetFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentServiceCollectionExtensions.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentCreationService.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentCreationCommand.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentDraft.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentReadModel.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentRepository.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentReader.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentSql.cs`
- `db/README.md`
- `db/rebuild/pos_sql_apply_order.txt`
- `db/scripts/README.md`
- `db/scripts/Invoke-PosDbChecks.ps1`
- `db/validation/pos_expected_inventory.json`
- `db/validation/pos_prohibited_patterns.json`
- `db/state/tables/pos.fiscal_documents.sql`
- `db/state/tables/pos.fiscal_sequence_states.sql`
- `db/state/tables/pos.idempotency_records.sql`
- `db/state/tables/pos.x_z_reports.sql`
- `db/state/tables/pos.bir_sales_summary_reports.sql`
- `db/state/tables/pos.electronic_journal_records.sql`
- `tests/ExitPass.PosServer.Runtime.Tests/FiscalDocumentCreationServiceTests.cs`
- `tests/ExitPass.PosServer.Api.Tests/FiscalDocumentCreationEndpointTests.cs`
- `tests/ExitPass.PosServer.Api.Tests/FiscalDocumentReadEndpointTests.cs`
- `tests/ExitPass.PosServer.Persistence.Postgres.Tests/PostgresFiscalDocumentRepositoryTests.cs`
- `tests/ExitPass.PosServer.Persistence.Postgres.Tests/PostgresFiscalDocumentReaderTests.cs`
- `tests/ExitPass.PosServer.Api.IntegrationTests/FiscalDocumentApiPostgresSmokeTests.cs`

ExitPass documentation references:

- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0_Alignment_Review.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
- `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`

## 21. Appendix B: Commands Run

Commands run in `D:\SourceCodes\ExitPass-PoSServer`:

```powershell
git status --short --untracked-files=all
git branch --show-current
git log --oneline -8
Get-ChildItem -Force | Select-Object Mode,Name,Length
rg --files
Get-ChildItem -Recurse -Filter *.sln | Select-Object FullName
Get-ChildItem -Recurse -Filter *.csproj | Select-Object FullName
rg -n "Map(Get|Post|Put|Delete)|MapFiscal|CreateFiscal|GetFiscal|FiscalDocument|Idempot|Sequence|SalesInvoice|ExitAuthorization|PaymentFinal|Gate|ManualRelease|Continuity|Discount|Payable" src tests -S
Get-Content -LiteralPath src\ExitPass.PosServer.Api\FiscalDocuments\FiscalDocumentEndpointRouteBuilderExtensions.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Api\FiscalDocuments\FiscalDocumentCreationEndpoint.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Api\FiscalDocuments\FiscalDocumentReadEndpoint.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Runtime\FiscalDocuments\FiscalDocumentCreationService.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Runtime\FiscalDocuments\FiscalDocumentCreationCommand.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Runtime\FiscalDocuments\FiscalDocumentDraft.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Runtime\FiscalDocuments\FiscalDocumentCreationResult.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Api\FiscalDocuments\CreateFiscalDocumentRequest.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Persistence.Postgres\FiscalDocuments\PostgresFiscalDocumentRepository.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Persistence.Postgres\FiscalDocuments\PostgresFiscalDocumentSql.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Persistence.Postgres\FiscalDocuments\PostgresFiscalDocumentReader.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Runtime\FiscalDocuments\FiscalDocumentReadModel.cs
rg -n "fiscal_sequence|fiscal_document_number|idempotency|lock|for update|next|sequence_value|Grand Total|grand_total|z_counter|reset_counter|x_z|bir_sales|poslog|electronic_journal|reprint|adjustment" db src tests -S
Get-Content -LiteralPath db\rebuild\pos_sql_apply_order.txt
Get-Content -LiteralPath db\README.md
Get-Content -LiteralPath db\scripts\README.md
Get-Content -LiteralPath db\scripts\Invoke-PosDbChecks.ps1
rg -n "\[Fact\]|public .* Task|public void|Assert\.Contains|Assert\.DoesNotContain|Assert\.Null|Assert\.Equal|Assert\.Single" tests\ExitPass.PosServer.Runtime.Tests tests\ExitPass.PosServer.Api.Tests tests\ExitPass.PosServer.Persistence.Postgres.Tests tests\ExitPass.PosServer.Api.IntegrationTests -S
Get-Content -LiteralPath README.md
Get-ChildItem -Recurse -File -LiteralPath .github | Select-Object FullName
Get-Content -LiteralPath .github\workflows\pos-db-validation.yml
Get-Content -LiteralPath src\ExitPass.PosServer.Api\Program.cs
Get-Content -LiteralPath src\ExitPass.PosServer.Api\FiscalDocuments\FiscalDocumentServiceCollectionExtensions.cs
rg -n "ExitAuthorization|open_gate|gate|payment finality|PaymentFinality|approve|entitlement|manual release|continuity" src db tests -S
(Get-ChildItem -LiteralPath db\state\tables -Filter *.sql | Measure-Object).Count
(Get-ChildItem -LiteralPath db\reference-data\controlled-codes\source\families -Filter *.json | Measure-Object).Count
(Get-ChildItem -Recurse -LiteralPath tests -Filter *.cs | Measure-Object).Count
(Get-ChildItem -Recurse -LiteralPath src -Filter *.cs | Measure-Object).Count
```

Commands run in `D:\SourceCodes\ExitPass`:

```powershell
git status --short --untracked-files=all
git branch --show-current
git diff --check
```
