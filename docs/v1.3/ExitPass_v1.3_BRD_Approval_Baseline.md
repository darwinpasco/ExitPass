# ExitPass v1.3 BRD Approval Baseline

Date: 2026-07-01
Status: Approved for v1.3 System Design baseline

## 1. Purpose

This note records approval of the completed ExitPass v1.3 BRD set as the business baseline for the next locked writing-order phase: ExitPass System Design v1.3.

This approval is a documentation baseline approval. It is not BIR accreditation approval, legal approval, final accounting/tax treatment approval, API approval, database design approval, or implementation approval.

## 2. Approved BRD Baseline List

| Document | Baseline status |
| --- | --- |
| ExitPass BRD v1.3 | Approved for v1.3 System Design baseline |
| ExitPass Assisted Payment Terminal BRD v1.0 | Approved for v1.3 System Design baseline |
| ExitPass Continuity BRD v1.0 | Approved for v1.3 System Design baseline |
| ExitPass Operator Console BRD v1.1 | Approved for v1.3 System Design baseline |
| ExitPass Management Dashboard and Reporting BRD v1.0 | Approved for v1.3 System Design baseline |
| ExitPass POS/Invoicing BRD v1.0 | Approved for v1.3 System Design baseline, with downstream finance/accounting and BIR/accreditation confirmations preserved |

## 3. Approval Date

Approval date: 2026-07-01.

## 4. Approval Meaning

These BRDs are approved as the business baseline for ExitPass System Design v1.3.

These BRDs preserve the v1.3 authority model:

- Vendor PMS / HCP remains authority for raw parking session lifecycle and normal tariff computation.
- Central PMS remains authority for payment-linked platform control state, payment finality, fiscal issuance reference recording, and ExitAuthorization.
- Payment Orchestrator reports verified provider outcomes and does not declare platform payment finality.
- POS Server remains fiscal issuance authority and does not issue ExitAuthorization.
- WebPay, Assisted Payment Terminal, Operator Console, Management Dashboard, and other channels/modules do not bypass Central PMS or POS Server authority.
- Gate/exit execution consumes Central PMS authorization and does not bypass Central PMS authority.

These BRDs are sufficient to proceed to the System Design phase. Open questions remain valid downstream design or confirmation items.

## 5. What Remains Open

The following items remain open and are not closed by this approval:

- POS/Invoicing BIR/accounting confirmation items.
- MIN/PTU/serial/software/supplier assignment.
- Tax/VAT treatment confirmation.
- Digital Sales Invoice URL security model.
- POS Server technical design details.
- Continuity activation authority.
- Degraded tariff freshness threshold.
- Exact dashboard/reporting implementation details.
- Exact API endpoint/DTO boundaries.
- Exact database deltas.
- Exact engineering implementation details.

## 6. Documents Now Approved as Inputs to ExitPass System Design v1.3

The following documents are approved as inputs to ExitPass System Design v1.3:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`

## 7. Documents Not Covered by This Approval

This approval does not cover:

- ExitPass System Design v1.3.
- Companion technical designs.
- Database Design / Database Delta.
- API Contract Pack v1.3.
- Engineering Pack v1.3.
- POS Server System Design.
- POS Server API Contract.
- Vendor PMS Connector System Design.
- HikCentral Connector Profile.
- Assisted Payment Terminal System Design.
- Continuity System Design.
- Test/UAT Pack.
- Operations Runbook Pack.
- BIR accreditation submission pack.

## 8. Next Locked Writing-Order Phase

The next locked writing-order phase is:

ExitPass System Design v1.3.

System Design v1.3 should use the approved BRD baseline as business input while preserving open downstream confirmation items for finance/accounting, BIR/accreditation, API, database, engineering, and implementation design.
