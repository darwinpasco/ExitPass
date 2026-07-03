# ExitPass FEQ First Runtime Foundation Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ First Runtime Foundation Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-inventory-persistence-intake-readback-prep |
| Scope | Central PMS runtime foundation |
| Status | implementation_note |

## Implemented Runtime Foundation

This slice adds the first Central PMS runtime foundation for the Fiscal Exception Queue (FEQ) without adding retry execution, a retry scheduler, Operator Console UI, Management Dashboard projection, or POS Server runtime changes.

Implemented:

- Internal FEQ application service for read-only list/detail over fiscal exception cases.
- FEQ case projection backed by existing `core.fiscal_issuance_references` persistence.
- Stable FEQ case identity using `fiscal_issuance_reference_id`.
- Duplicate collapse by source fiscal issuance reference identity.
- Safe error summary based on exception reason/error code only.
- Readback preparation contract that does not call POS Server.
- Retry eligibility placeholder that keeps retry execution unavailable in this slice.

## Detectable Intake Categories

The first slice maps existing Central PMS fiscal issuance reference states and exception reasons into FEQ categories for:

- POS Server unavailable/service failure.
- POS Server timeout.
- POS Server HTTP/failure posture where recorded by existing fiscal reference state.
- Unknown outcome requiring readback.
- POS Server accepted but Central PMS recording/persistence failed where represented by existing exception reasons.
- Idempotency conflict.
- Semantic request hash/replay mismatch.
- Fiscal configuration missing.
- Central PMS mapping/request construction failure.
- Manual review and fiscal mismatch.

## Persistence Decision

No new FEQ table or migration is introduced in this slice. Existing fiscal issuance reference persistence already contains the minimum safe source identity, state, context, error posture, timestamps, and fiscal evidence fields required to expose a first FEQ runtime projection.

Future slices may add dedicated FEQ case tables if assignment, SLA, closure history, or richer workflow state cannot be represented safely by fiscal reference records.

## Boundaries Preserved

- FEQ remains a recovery coordinator, not a payment, fiscal numbering, ExitAuthorization, or gate authority.
- No payment finality mutation is introduced.
- No ExitAuthorization issuance is introduced.
- No gate behavior is introduced.
- No fiscal document creation or fiscal number editing is introduced.
- No retry execution is introduced.
- Readback is prepared as a contract only; no POS Server readback call is made by this slice.

## Follow-Up Slices

- Add explicit FEQ API surface with RBAC/reporting scope.
- Add readback worker and classification using approved POS Server GET/readback APIs.
- Add FEQ assignment/SLA workflow persistence if required.
- Add retry eligibility evaluator after readback classification exists.
- Add controlled retry scheduler only after readback and eligibility slices pass review.

