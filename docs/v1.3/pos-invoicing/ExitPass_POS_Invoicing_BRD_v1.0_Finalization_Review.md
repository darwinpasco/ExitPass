# ExitPass POS/Invoicing BRD v1.0 Finalization Review

Date: 2026-07-01  
Branch: docs/v1.3-pos-invoicing-brd-finalize  
Document reviewed: `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`

## Review Summary

The ExitPass POS/Invoicing BRD v1.0 was reviewed and finalized as a companion business and compliance requirements document for ExitPass v1.3. The document is aligned to the approved platform-wide Site-level POS Server model, Sales Invoice fiscal output, fiscal issuance before ExitAuthorization, and preserved Central PMS / POS Server / channel authority boundaries.

Recommendation: ready for business review.

## Files Reviewed

- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_Decision_Log.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_Open_Questions.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_Source_Analysis.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Server_Impact_Map.md`
- Existing POS/Invoicing diagrams under `docs/v1.3/pos-invoicing/diagrams/`
- Supporting POS Server documents under `docs/v1.3/pos-server/`, `docs/v1.3/pos-server-api/`, and `docs/v1.3/pos-server-db/` were treated as references only.

## Source Baselines Used

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md`
- `docs/v1.3/ExitPass_v1.3_Documentation_Outline.md`
- `docs/v1.3/ExitPass_v1.3_Open_Questions.md`
- `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `D:\Docs\ExitPass\v1.2`
- `D:\Docs\ExitPass\POS`

## Alignment With ExitPass BRD v1.3

The finalized POS/Invoicing BRD preserves the v1.3 authority model:

- Central PMS remains payment finality and ExitAuthorization authority.
- POS Server remains fiscal issuance authority.
- Fiscal issuance must succeed before Central PMS issues normal ExitAuthorization.
- WebPay, APM, Assisted Payment Terminal modes, Operator Console, Management Dashboard, and future channels do not become independent fiscal authorities.

## Alignment With Assisted Payment Terminal BRD

The BRD now uses Cashier-Assisted Terminal and Continuity Terminal terminology aligned with the Assisted Payment Terminal BRD. It confirms:

- Assisted Payment Terminal is payment-capable but not payment finality authority.
- Cashier-Assisted Terminal can capture statutory discount validation inputs.
- Central PMS / Discount workflow owns statutory validation persistence and policy resolution.
- Fiscal issuance routes through the resolved Site POS Server.

## Alignment With Continuity BRD

The BRD aligns continuity fiscal handling with controlled degraded operation:

- No silent fallback.
- Continuity Terminal disabled by default.
- Continuity does not automatically permit offline fiscal issuance.
- Fiscal exceptions and manual release require supervisor, incident, audit, and reconciliation controls where allowed.
- Post-restoration reconciliation remains required for continuity-origin activity.

## Alignment With Operator Console BRD

The BRD preserves Operator Console as non-payment and non-fiscal. Operator Console may review fiscal exceptions, statutory discount evidence, continuity activation, manual release governance, and audit records, but it does not issue Sales Invoices, mutate fiscal records, collect payment, declare finality, or issue ExitAuthorization.

## Alignment With Management Dashboard and Reporting BRD

The BRD preserves the reporting boundary:

- Management Dashboard is visibility/reporting only.
- Operational visibility is not financial truth.
- Fiscal dashboards reconcile to POS Server fiscal records and Central PMS fiscal references.
- Dashboard does not issue fiscal documents.

## BIR/POS Reference Alignment

The finalized BRD incorporates high-level business requirements from BIR/POS references:

- Sales Invoice as primary parking fiscal output.
- X-read and Z-read.
- BIR Sales Summary / Annex E-1 as first-class required fiscal reporting.
- Annex E-2 to E-5 retained in the extensible model for Senior Citizen, PWD, NAAC, and Solo Parent reporting where applicable.
- Reprint controls requiring `REPRINT` and `DATE / TIME REPRINTED`.
- Reset counter, Z-counter, and Grand Total Amount controls.
- Taxpayer/fiscal identity and supplier accreditation footer support.
- Electronic Journal, POSLog, export, audit, and retention posture.
- BIR RMO No. 10-2019 Diplomat VAT Privilege / VAT Exemption treated as VAT privilege/exemption, not ordinary discount.

## ARTS POSLog Posture

ARTS POSLog v6.0 is captured as a supporting structured export/schema reference only. The BRD states that ARTS supports structured transaction/export modeling and extension points, but it does not override Philippine BIR fiscal document/report requirements. Final schema/profile/export packaging remains open for BIR/accreditation and technical design.

## Authority-Boundary Review

No authority conflicts were found after finalization. The BRD explicitly states:

- POS/Invoicing does not declare payment finality.
- POS/Invoicing does not issue ExitAuthorization.
- POS/Invoicing does not open gates.
- POS/Invoicing does not replace Central PMS or Vendor PMS authority.
- Payment channels and terminals do not become independent fiscal authorities.
- Projection data is not financial truth.
- Statutory entitlement approval remains with Central PMS / Discount workflow.

## Open Questions Retained

The finalized BRD retains downstream confirmation items only, including:

- MIN/PTU/serial/software/supplier assignment.
- Taxpayer/Site/branch/Site POS Server/channel fiscal identity assignment.
- WebPay fiscal terminal identity.
- Sales Invoice and adjustment document numbering patterns.
- Sequence gaps, reserved numbers, failed issuance, and abandoned issuance.
- X-read and Z-read aggregation scope.
- VAT/tax treatment.
- Diplomat VAT treatment, evidence, wording, reporting, and retention.
- Digital SI URL token/access/expiry/authentication model.
- ARTS POSLog export profile/schema mapping.
- JSON schema versioning and validation strategy.
- Accreditation sample package.
- Tamper-evident anchoring/recovery mechanism.
- Endpoint names, DTO boundaries, database tables/columns, event payloads, and permission matrix/RBAC.

## Issues Found

- The prior draft contained the required decisions but did not follow the requested finalization section order.
- The prior draft used older Cashier POS / EC Device naming in some places; the finalized draft aligns terminology to Cashier-Assisted Terminal and Continuity Terminal while preserving channel coverage.
- Existing diagrams were acceptable and did not require regeneration.

## Changes Made

- Reorganized the BRD into the required 44-section finalization structure.
- Strengthened the platform-wide POS/Invoicing positioning.
- Strengthened Sales Invoice terminology and avoided treating Official Receipt / OR as the primary parking fiscal output.
- Added explicit relationships to ExitPass BRD v1.3, Assisted Payment Terminal, Continuity, Operator Console, and Management Dashboard and Reporting.
- Added explicit Digital Sales Invoice URL and QR presentation responsibilities.
- Added local ordinance/policy registry alignment through Site jurisdiction and active policy controls.
- Added stronger DR/restore, counter integrity, and fiscal continuity requirements.
- Added clear ARTS POSLog posture.
- Preserved existing diagram references and verified both `.puml` and `.jpg` files exist.

## Remaining Downstream Actions

- Business review by product, finance/accounting, operations, and compliance.
- BIR/accounting confirmation of taxpayer/fiscal identity assignment, tax treatment, numbering, report scope, and accreditation sample package.
- POS Server System Design after companion BRDs are accepted.
- POS Server API Contract after POS Server design.
- Database Delta / Data Dictionary refresh after System Design direction.
- Engineering Pack and UAT coverage after design and contract stabilization.

## Recommendation

Ready for business review.
