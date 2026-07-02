# Central PMS Fiscal Issuance Engineering Pack Detail Plan 01: Database / State Delta Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Database / State Delta Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines Central PMS persistence and state planning for fiscal issuance references. It does not write SQL, migrations, schema DDL, generated artifacts, or implementation code.

## Purpose

Central PMS must durably record POS Server fiscal issuance evidence before normal ExitAuthorization can proceed. This plan identifies candidate storage objects, candidate fields, linkage, uniqueness, idempotency, audit, and migration sequencing needs for a later database delta.

## Source Inputs

- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`

## Candidate Storage Objects

Candidate objects for later database design confirmation:

- Fiscal issuance reference record, linked to a Central PMS payment confirmation and parking session.
- Fiscal issuance attempt/history record, recording each create, retry, replay, conflict, readback, and operator-triggered action.
- Fiscal issuance exception state record or state fields, supporting review and reconciliation.
- Fiscal readback/reconciliation record, supporting unknown outcome recovery and mismatch tracking.
- Audit/correlation extension fields, if existing audit storage cannot cover required traceability.

These are planning objects only. Final table names, column names, keys, constraints, indexes, and migration order remain deferred.

## Candidate Fiscal Reference Fields

Central PMS should plan storage for at least:

| Candidate field | Source / purpose |
| --- | --- |
| POS Server fiscal document id | Returned by POS Server create/read; primary fiscal document reference. |
| fiscal identity id | POS Server-resolved identity used for fiscal issuance. |
| fiscal sequence policy id | POS Server-resolved sequence policy. |
| fiscal sequence value | Allocated fiscal sequence value. |
| fiscal document number | Assigned Sales Invoice/fiscal document number. |
| fiscal series | Fiscal series at assignment time. |
| fiscal number prefix text | Prefix at assignment time. |
| fiscal number suffix text | Suffix at assignment time. |
| fiscal number assigned at | POS Server assignment timestamp. |
| fiscal number assigned by ref | POS Server assignment actor/reference. |
| fiscal document status code id | POS Server persisted status code id. |
| result classification | `newly_created` or `idempotent_replay`. |
| fiscal issuance evidence status | Expected complete evidence: `fiscal_document_number_assigned`. |
| fiscal number assignment state | Expected complete state: `assigned`. |
| upstream finality reference | Current POS Server idempotency key source. |
| Central PMS payment confirmation ref | Links to Central PMS payment finality. |
| Central PMS payment attempt ref | Links to payment attempt history. |
| Central PMS parking session ref | Links to session/control context. |
| Site id | Reporting and attribution boundary. |
| Site POS Server id | Fiscal routing boundary. |
| request/correlation id | Cross-service traceability. |
| request hash or semantic hash reference if available | Supports replay/mismatch investigation. |
| POS Server response timestamp | Reconciliation and timing. |
| current fiscal issuance integration state | Drives gating, queues, and dashboards. |
| exception state/reason if applicable | Drives manual review and reconciliation. |

## Field Mapping from POS Server Response

Central PMS should map POS Server response fields without changing their authority meaning:

- POS Server fiscal identity and numbering fields become Central PMS fiscal issuance reference evidence.
- `resultClassification = newly_created` means POS Server created a new persisted numbered fiscal document.
- `resultClassification = idempotent_replay` means POS Server returned the original persisted numbered fiscal document for the same idempotency key and semantic request hash.
- `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned` is fiscal issuance evidence only.
- `fiscalNumberAssignmentState = assigned` is required before normal ExitAuthorization gating can pass.
- POS Server response does not become payment finality, gate permission, entitlement approval, manual release approval, or continuity activation.

## Candidate Fiscal Issuance States

Candidate state names pending engineering and database confirmation:

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

These labels are candidate planning values only. Final enum names, API status names, database values, and transition constraints remain deferred.

## Exception State / Reason Planning

Exception reason buckets should include:

- idempotency conflict
- request construction error
- unapproved discount reference
- sensitive payload rejection
- fiscal identity missing, ambiguous, inactive, or not effective
- fiscal sequence policy missing, ambiguous, inactive, or not effective
- fiscal sequence state missing, inactive, unsafe, or not effective
- allocation or format failure
- persistence unavailable
- incomplete numbering evidence
- unknown POST outcome
- GET readback inconclusive
- Central PMS fiscal reference mismatch
- POS Server readback mismatch
- manual release request after fiscal issuance failure

## Attempt / History Model

Central PMS should preserve fiscal issuance integration history, including:

- attempt number or sequence
- trigger source: automatic, operator-triggered, reconciliation-triggered
- request correlation id
- upstream finality reference used
- POS Server response code and error code
- `resultClassification`
- `errorPosture`
- fiscal document id when returned
- success, replay, conflict, failure, or unknown classification
- timestamp and actor/service identity
- readback action and readback result

Attempt history must make replay and conflict visible without creating duplicate fiscal references.

## Retry, Replay, and Conflict History

The future persistence model should support:

- same `payableBasis.upstreamFinalityRef` for retry of the same fiscal issuance attempt
- uniqueness/idempotency protection around upstream finality reference within the proper fiscal issuance scope
- same-key/same-hash replay recording without new fiscal reference duplication
- same-key/different-hash conflict recording as fail-closed
- prevention of a new upstream finality reference being used to bypass conflict unless a formal supervised correction process is later approved

## Unknown Outcome / Readback Fields

Plan fields or records for:

- unknown outcome reason
- whether fiscal document id was returned before failure
- whether GET readback was attempted
- readback timestamp and result
- readback fiscal document id and number
- mismatch reason if readback differs from Central PMS expectation
- reconciliation closure status and closure actor

## Linkage Requirements

Fiscal issuance reference data should link to:

- payment confirmation
- payment attempt
- parking session
- payable basis / TariffSnapshot equivalent
- discount validation reference where applicable
- Site
- Site POS Server
- customer channel or terminal context where relevant
- ExitAuthorization decision record when issued
- manual release exception record when normal ExitAuthorization remains blocked

## Audit Fields

Plan audit fields for:

- created at / by service identity
- updated at / by service identity
- first recorded at
- last retry at
- last readback at
- manual review actor and decision
- reconciliation actor and closure timestamp
- incident or exception reference where applicable

Soft-delete should generally not apply to fiscal issuance references. If archival flags are needed, they must preserve auditability and not hide fiscal evidence from reconciliation.

## Migration Sequencing Plan

Recommended sequencing for later database delta work:

1. Add candidate reference/state storage in a non-invasive way.
2. Backfill neutral state for existing paid transactions where fiscal issuance is not yet integrated.
3. Add read paths for internal diagnostics before enforcing gating.
4. Add uniqueness/idempotency protection after data profile review.
5. Add state transition protections after service behavior is implemented.
6. Enable ExitAuthorization gating only after persistence and orchestration are validated.

This plan intentionally does not include SQL or migration scripts.

## Data Backfill / Open Migration Questions

- How should existing paid transactions be classified before fiscal issuance integration goes live?
- Are historical payment confirmations required to receive `not_required`, `pending_fiscal_issuance`, or another migration state?
- Is Site POS Server mapping complete for all production Sites before enabling enforcement?
- What is the approved retention policy for fiscal issuance attempt history?
- Which existing Central PMS identifiers are stable enough for fiscal reference linkage?

## Risks and Open Questions

- Central PMS schema may not yet support all POS Server fiscal reference fields.
- Existing payment and ExitAuthorization flows may assume payment success is sufficient for exit eligibility.
- Fiscal reference persistence failure after POS Server success requires careful recovery design.
- Final state names and storage boundaries remain engineering decisions.
- Final uniqueness scope for `payableBasis.upstreamFinalityRef` must match POS Server idempotency scope.

## Authority Boundary

Central PMS owns payment finality, fiscal reference recording, and normal ExitAuthorization. POS Server owns fiscal issuance and numbering only. This persistence plan must not make POS Server an exit authority or make Central PMS a fiscal document issuer.
