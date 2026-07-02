# ExitPass Central PMS Fiscal Reference State Implementation Slice Plan v1.0 Review

## Review Summary

The first Central PMS fiscal reference state implementation-slice planning package was created for `feature/central-pms-fiscal-reference-state`.

The package is documentation-only. It prepares implementation handoff for persistence/state scaffolding only and explicitly excludes POS Server network calls, retry scheduler work, Operator Console implementation, Dashboard implementation, ExitAuthorization gating enforcement, source code changes, SQL, migrations, generated artifacts, and DOCX changes in this task.

## Branch Name

`docs/v1.3-central-pms-fiscal-reference-state-implementation-plan`

## Files Created

- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Fiscal_Reference_State_Implementation_Slice_Plan_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Fiscal_Reference_State_Implementation_Slice_Plan_v1.0_Review.md`

## Runtime Repo Inspected

Runtime repository inspected read-only:

`D:\SourceCodes\ExitPass-PoSServer`

Runtime branch:

`dev`

Runtime reference documents inspected:

- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

The runtime repository was not modified.

## Docs Inspected

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/01-database-state-delta-plan.md`
- `docs/v1.3/central-pms/engineering-pack/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Detail_v1.0.md`
- `docs/v1.3/central-pms/database-delta/ExitPass_Central_PMS_Fiscal_Reference_Persistence_Database_Delta_Plan_v1.0.md`
- `docs/v1.3/central-pms/state-taxonomy/ExitPass_Central_PMS_Fiscal_Issuance_State_Transition_and_Exception_Taxonomy_Plan_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

## Repository / Schema Inspection Summary

Repository inspection found:

- Central PMS service under `src/Services/CentralPms`.
- API, Application, Contracts, Domain, and Infrastructure projects.
- Unit, Integration, and Contract test projects.
- Npgsql DB routine gateways in Infrastructure.
- payment/exit application handlers and gateway abstractions.
- domain enum classes using PascalCase values.
- database enum values in the v1.2 DDL baseline using uppercase string values.
- DB patch and validation patch conventions under `infra/db/patches`.
- existing core objects including `core.payment_attempts`, `core.payment_confirmations`, `core.exit_authorizations`, `core.parking_sessions`, and `core.tariff_snapshots`.
- existing uniqueness patterns for payment attempt idempotency, payment confirmation per attempt, and ExitAuthorization per payment attempt/confirmation.

## Current Central PMS Implementation Observations

- Payment confirmation is recorded through `RecordPaymentConfirmationService` and `RecordPaymentConfirmationGateway`.
- `RecordPaymentConfirmationGateway` calls `core.record_payment_confirmation(...)`.
- Payment confirmation endpoint requires correlation and idempotency headers.
- Payment confirmation validates payable-basis consistency before recording provider evidence.
- ExitAuthorization is issued through `IssueExitAuthorizationHandler` and DB-backed gateway.
- Current ExitAuthorization path enforces payment finality, but fiscal reference gating is not yet present.
- Existing integration tests exercise DB routine gateways against seeded database state.
- Existing event names are centralized in `IntegrationEventTypes`.
- Existing metrics are centralized in `CentralPmsMetrics`.
- No Central PMS fiscal issuance reference persistence object was found in source inspection.
- No Central PMS POS Server fiscal issuance network client was found, aligning with this slice's non-goals.

## Candidate Persistence Objects

Candidate objects:

- fiscal issuance reference
- fiscal issuance attempt/history
- fiscal issuance exception/review state
- fiscal readback/reconciliation record

Recommended safer path:

- dedicated fiscal reference persistence linked to payment confirmation, payment attempt, parking session, Site, and Site POS Server.

Not recommended as the primary path:

- overloading `core.payment_confirmations` with all fiscal issuance state, because that risks mixing payment finality with fiscal issuance evidence.

## Candidate Field Groups

Field groups documented:

- fiscal reference fields
- attempt/history fields
- exception/review fields
- readback/reconciliation fields
- audit/correlation fields
- linkage fields to payment confirmation, payment attempt, parking session, Site, Site POS Server, payable basis, future ExitAuthorization, and manual release exception records

## Candidate State Values

Candidate states documented:

- `not_required`
- `pending_fiscal_issuance`
- `fiscal_issuance_requested`
- `fiscal_issuance_recorded`
- `fiscal_issuance_replayed`
- `fiscal_issuance_conflict`
- `fiscal_issuance_failed_request`
- `fiscal_issuance_failed_configuration`
- `fiscal_issuance_failed_service`
- `fiscal_issuance_unknown`
- `fiscal_issuance_manual_review`
- `fiscal_issuance_exception_released`
- `fiscal_issuance_reconciled`

## Candidate Exception Reasons

Reason groups documented:

- request/data reasons
- configuration/fiscal setup reasons
- conflict/replay reasons
- service/unknown reasons
- review/manual release reasons

## Uniqueness / Idempotency Checklist

Checklist includes:

- upstream finality reference uniqueness within POS Server-compatible fiscal issuance idempotency scope.
- scope alignment to fiscal document creation operation + Site POS Server id + fiscal document type code id + upstream finality reference.
- no duplicate active fiscal reference for replay.
- POS Server fiscal document id not mapped to multiple active Central PMS fiscal references.
- fiscal document number uniqueness within Site POS Server / fiscal identity / sequence policy context, subject to final rules.
- uniqueness safeguards introduced only after data profile review.

## Validation / Test Checklist

Checklist includes:

- create fiscal reference persistence object.
- create attempt/history record.
- create exception/review state.
- create readback/reconciliation shell if included.
- persist POS Server fiscal fields.
- persist state and exception values.
- block or flag duplicate upstream finality scope.
- prevent replay duplicate fiscal reference.
- preserve incomplete evidence as not assigned / not gating ready.
- persist unknown outcome.
- exclude sensitive payload fields.
- populate audit fields.
- prove future ExitAuthorization gating data-readiness.

## Strict Non-Goals

Preserved non-goals:

- no POS Server client implementation.
- no POS Server network calls.
- no request mapper implementation.
- no retry scheduler.
- no GET readback worker.
- no ExitAuthorization gating enforcement.
- no Operator Console queues.
- no Dashboard projections.
- no SQL DDL in this document.
- no source code changes in this task.
- no migrations in this task.

## Authority Boundaries Preserved

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- Operator Console remains governance/review only.
- Management Dashboard remains visibility/reporting only.

## Blockers or Mismatches

No source contradiction was found.

Open implementation blockers:

- Central PMS schema conventions require deeper implementation-time inspection.
- Site POS Server identity/storage may not yet exist in Central PMS.
- Fiscal document type code id may not yet exist in Central PMS.
- Final database representation of candidate states is undecided.
- Uniqueness constraints should wait for data profile review.

## Recommended Implementation Branch

`feature/central-pms-fiscal-reference-state`

## Recommended First Codex Implementation Task

Implement Central PMS fiscal reference state persistence scaffolding only: inspect schema conventions, create the database delta and validation plan, add candidate persistence models/repository or gateway interfaces, and add tests for storing fiscal reference state without POS Server network calls or ExitAuthorization gating enforcement.

## Validation Results

Validation executed:

- `git diff --check` completed with no whitespace errors.
- `git status --short --untracked-files=all` showed only the two new Markdown files under `docs/v1.3/central-pms/implementation-slices/`.
- Obsolete primary terminology scan completed with no matches in the created implementation-slice files.
- Runtime repository status check showed no changes in `D:\SourceCodes\ExitPass-PoSServer`.
