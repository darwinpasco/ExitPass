# Central PMS Fiscal Issuance Engineering Pack Detail Plan 04: Success / Replay Recording Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Success / Replay Recording Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines Central PMS handling for successful POS Server fiscal issuance and idempotent replay. It does not implement source code, SQL, API contracts, or tests.

## Purpose

Central PMS must record fiscal issuance evidence after POS Server returns a complete successful response. It must also treat idempotent replay as successful evidence only when the replayed response is complete and matches the expected Central PMS context.

## Handling `202 accepted` + `newly_created`

When POS Server returns:

- HTTP `202 Accepted`
- `code = accepted`
- `resultClassification = newly_created`
- `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`
- `fiscalNumberAssignmentState = assigned`

Central PMS should:

- validate required fiscal numbering fields are present.
- validate response correlation against Site, Site POS Server, payment confirmation, session, and upstream finality reference.
- durably record the fiscal issuance reference.
- record result classification and POS Server response timestamp.
- transition to a recorded fiscal issuance state.
- allow normal ExitAuthorization evaluation only after durable reference recording succeeds.

## Handling `202 accepted` + `idempotent_replay`

When POS Server returns an idempotent replay:

- treat it as successful fiscal issuance evidence only if evidence status and assignment state are complete.
- record or reconcile the original POS Server fiscal document id and numbering fields.
- avoid creating duplicate Central PMS fiscal reference records.
- compare returned fiscal document id/number against any existing Central PMS reference.
- escalate mismatch to manual review.
- avoid duplicate ExitAuthorization issuance.

Replay must not be treated as a new fiscal issuance event for numbering or exit purposes.

## Fiscal Reference Recording Rules

Central PMS should record:

- POS Server fiscal document id
- fiscal identity id
- fiscal sequence policy id
- fiscal sequence value
- fiscal document number
- fiscal series
- fiscal number prefix/suffix
- fiscal number assigned at/by
- fiscal document status code id
- result classification
- fiscal issuance evidence status
- fiscal number assignment state
- upstream finality reference
- payment confirmation ref
- payment attempt ref
- parking session ref
- Site and Site POS Server refs
- response timestamp
- correlation id

If Central PMS cannot durably record these fields, fiscal issuance remains unresolved for ExitAuthorization gating.

## Duplicate Prevention

The future implementation should prevent:

- multiple fiscal reference records for the same payment finality where one valid fiscal reference already exists
- replay recording as a new fiscal document
- a duplicate ExitAuthorization decision caused by retry/replay
- operator queue duplication for the same resolved fiscal reference

Uniqueness and transaction details remain deferred to database/engineering work.

## Replay Reconciliation

Replay reconciliation should:

- confirm returned fiscal document id matches any existing reference for the upstream finality reference.
- confirm fiscal number fields match previously recorded values.
- preserve replay attempt history.
- leave the primary fiscal reference unchanged when replay matches.
- transition or annotate state as replayed when needed for audit.

## Mismatch Detection

Central PMS should detect:

- returned fiscal document id differs from previously recorded reference.
- returned fiscal document number differs from previously recorded number.
- returned Site POS Server context differs from expected context.
- returned evidence status or assignment state is incomplete.
- readback result differs from create/replay result.

Mismatch must fail closed and route to manual review/reconciliation.

## No Duplicate ExitAuthorization

Replay does not justify a second ExitAuthorization. If ExitAuthorization was already issued after a valid fiscal reference, replay should reconcile only. If ExitAuthorization was not yet issued, replay can satisfy fiscal evidence requirements only after durable Central PMS recording.

## State Transitions

Candidate transitions:

- `fiscal_issuance_requested` to `fiscal_issuance_recorded` on `newly_created` success and durable record.
- `fiscal_issuance_requested` or `fiscal_issuance_unknown` to `fiscal_issuance_replayed` on replay success and reconciliation.
- any success/replay with mismatch to `fiscal_issuance_manual_review`.

Final transition names remain deferred.

## Audit Events

Candidate audit/events:

- `FiscalIssuanceRecorded`
- `FiscalIssuanceReplayed`
- `FiscalIssuanceManualReviewRequired`
- `ExitAuthorizationBlockedByFiscalState` when recording fails

Event names are placeholders pending engineering conventions.

## Operator Visibility Conditions

Operator Console queue visibility should be generated when:

- replay matches but Central PMS reference was missing
- replay conflicts with existing Central PMS reference
- Central PMS cannot persist a successful response
- evidence is incomplete
- ExitAuthorization remains blocked after payment finality

Operator Console remains review/governance only.

## Test Scenarios

Future tests should cover:

- first-time successful record
- idempotent replay with no existing Central PMS reference
- idempotent replay with matching existing reference
- idempotent replay with mismatched existing reference
- duplicate replay does not create duplicate reference
- duplicate replay does not issue duplicate ExitAuthorization
- persistence failure after POS Server success leaves fiscal state unresolved

## Risks and Open Questions

- Durable transaction boundary for fiscal reference recording is not yet defined.
- Exact mismatch policy and closure authority remain open.
- Existing ExitAuthorization flow may need idempotency hardening.

## Authority Boundary

POS Server success/replay is fiscal issuance evidence only. Central PMS remains the only authority for payment finality, fiscal reference recording, and normal ExitAuthorization.
