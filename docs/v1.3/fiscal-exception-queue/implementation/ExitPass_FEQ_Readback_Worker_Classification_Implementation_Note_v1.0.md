# ExitPass FEQ Readback Worker and Classification Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Readback Worker and Classification Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-readback-worker-classification |
| Scope | Central PMS runtime readback/classification slice |
| Status | implementation_note |

## Runtime Behavior Added

This slice adds a backend-only Central PMS FEQ readback worker and classification path.

Implemented:

- `IFiscalExceptionReadbackWorker` for backend readback execution.
- `IFiscalExceptionReadbackClient` adapter over the existing POS Server fiscal document client.
- Safe POS Server GET readback by known `pos_server_fiscal_document_id` only.
- Readback classifications: `matched`, `not_found`, `mismatch`, `failed`, `unavailable`, `unknown`, `identifier_missing`, and `not_supported_yet`.
- FEQ read-only projection fields for readback classification and last readback attempt timestamp when represented by fiscal reference state.
- Safe logging of readback classification attempts.

## Identifier and Matching Rules

The worker calls POS Server only when the FEQ case already has a known POS Server fiscal document id.

The worker does not:

- fabricate identifiers;
- search broadly by fiscal number;
- use fiscal number as the only matching key;
- call POS Server from Operator Console or Management Dashboard;
- expose raw POS Server payloads.

A readback result is classified as `matched` only when the POS Server readback fiscal document id matches the Central PMS fiscal reference. Conflicting known evidence is classified as `mismatch`.

## Central PMS State Updates

Negative classifications use existing Central PMS fiscal reference readback-planning transitions:

- `not_found` -> `GetReadbackNotFound`
- `mismatch` -> `FiscalReferenceMismatch` and manual-review posture
- `failed` / `unavailable` -> `GetReadbackServiceFailed`
- `unknown` -> `GetReadbackInconclusive`

`matched` is returned as recovery/visibility evidence only in this slice. It does not mark the fiscal reference as recorded, reconciled, BIR-ready, or fiscal-gating-ready.

## Boundaries Preserved

- No retry execution.
- No retry scheduler.
- No retry endpoint.
- No fiscal-gated ExitAuthorization enforcement.
- No payment finality mutation.
- No ExitAuthorization issuance.
- No gate behavior.
- No fiscal number editing.
- No manual fiscal document creation.
- No POS Server runtime changes.

## Follow-Up Slices

- Persist durable FEQ readback attempt history if shell-table usage is approved.
- Add RBAC-governed internal FEQ API surface for read-only visibility.
- Add reconciliation handling for matched readback evidence after POS Server fiscal-numbering/BIR readiness is approved.
- Add retry eligibility evaluator only after readback classification and durable evidence/audit are accepted.

