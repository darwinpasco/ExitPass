# ExitPass FEQ Readback Attempt Persistence / Audit Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Readback Attempt Persistence / Audit Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-readback-attempt-persistence-audit |
| Scope | Central PMS runtime foundation for durable FEQ readback attempt history |
| Status | implemented_for_review |

## Purpose

This slice persists durable FEQ readback attempt history before any retry eligibility or retry execution work. It keeps FEQ as a recovery coordinator and preserves Central PMS/POS Server authority boundaries.

## Persistence Approach

The implementation reuses the existing Central PMS table `core.fiscal_issuance_readback_reconciliations` instead of adding a new broad FEQ workflow schema. The table already provides the minimum durable attempt/audit scaffold needed for this slice:

| Required fact | Persistence mapping |
| --- | --- |
| Readback attempt identity | `fiscal_issuance_readback_id` |
| FEQ/fiscal issuance reference identity | `fiscal_issuance_reference_id` |
| Payment confirmation anchor | `payment_confirmation_id` |
| Attempted timestamp | `readback_requested_at` and `readback_completed_at` |
| Classification | `comparison_result` plus safe `readback_result_code` |
| Identifier used | `pos_server_fiscal_document_id` when available; safe summary notes identifier posture |
| POS Server document id | `pos_server_fiscal_document_id` |
| Safe error/result code | `readback_result_code` |
| Safe error summary | `mismatch_reason` |
| Service identity | `actor_service_identity_id` |

No raw POS Server payloads, secrets, payment provider payloads, or mutable fiscal number fields are stored by this slice.

## Worker Flow Changes

The FEQ readback worker now writes a readback attempt record for each classification attached to an existing FEQ/fiscal issuance reference case:

- `matched`
- `not_found`
- `mismatch`
- `failed`
- `unavailable`
- `unknown`
- `identifier_missing`
- `not_supported_yet`

The write happens before any fiscal reference state planning update. If attempt persistence fails, the worker does not proceed as if the readback attempt is fully auditable and does not schedule retry.

The only non-persisted worker outcome remains `feq_case_not_found`, because there is no fiscal issuance reference or payment confirmation anchor for a durable readback attempt row.

## FEQ Read-Only Detail Changes

FEQ detail can now include safe readback attempt summary data:

- last readback classification;
- last readback attempt timestamp;
- readback attempt count;
- last safe readback summary.

The FEQ list surface remains lightweight and does not perform per-row attempt-history lookups.

## Explicit Non-Goals

This slice does not implement:

- retry eligibility;
- retry execution;
- retry scheduler;
- retry endpoint;
- Operator Console UI;
- Management Dashboard projection;
- fiscal-gated ExitAuthorization enforcement;
- POS Server runtime/API changes;
- fiscal number editing;
- manual fiscal document creation.

## Validation Notes

Unit coverage was added for readback attempt persistence across matched, not found, mismatch, failed, unavailable, unknown, identifier missing, and not supported classifications. Tests also confirm FEQ detail returns last-attempt summary data and persistence failure does not enable retry or state mutation.

## Follow-Up Slice

Recommended next branch:

`feature/central-pms-feq-retry-eligibility-evaluator`

Purpose:

Implement retry eligibility evaluation only, using persisted readback attempt history and the readback-before-retry rule. Do not implement retry execution or a scheduler in that slice.
