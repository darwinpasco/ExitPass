# ExitPass POS Server System Design v1.0 Alignment Review

## 1. Review Summary

This review aligns `ExitPass_POS_Server_System_Design_v1.0.md` with the approved ExitPass v1.3 architecture baseline, companion BRDs, companion technical designs, and POS/Invoicing BRD v1.0.

The POS Server System Design remains a technical architecture document for the Site-level POS Server. This review did not rewrite the document from scratch and did not finalize API contracts, database design, implementation classes, endpoint paths, DTOs, fiscal layouts, accreditation package content, UAT scripts, or runbook procedures.

Recommendation: ready for follow-on POS Server API Contract, POS Server Database Design, and downstream finance/accounting/BIR confirmation review, subject to the open questions retained in the design.

## 2. Files Reviewed

Primary file updated:

- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`

Review note created:

- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0_Alignment_Review.md`

Approved v1.3 sources reviewed:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md`

Planning artifacts reviewed:

- `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md`
- `docs/v1.3/ExitPass_v1.3_Open_Questions.md`
- `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md`

## 3. v1.3 Alignment Summary

The POS Server System Design now explicitly references the approved v1.3 architecture baseline and companion technical designs as alignment sources.

The updated design preserves the v1.3 model:

- POS Server is resolved Site fiscal issuance authority.
- Central PMS remains payment finality, fiscal reference recording, degraded resolve, and ExitAuthorization authority.
- Payment channels and terminals remain channels under Central PMS payment authority and Site POS Server fiscal authority.
- Fiscal issuance must precede normal ExitAuthorization unless a separately approved exception/manual-release policy applies.
- Offline fiscal issuance remains restricted/open and is not approved by continuity mode.

## 4. Authority Boundary Review

The design now states that POS Server is not:

- payment finality authority
- payment provider authority
- Vendor PMS authority
- Central PMS authority
- parking session authority
- statutory entitlement authority
- continuity decisioning authority
- manual release approver
- gate authority
- ExitAuthorization issuer

The authority table was updated to include Central PMS / Discount workflow ownership of statutory discount policy resolution and payable-basis update. POS Server applies approved fiscal treatment from upstream context and does not mutate payable basis directly.

## 5. Site-level POS Server Model Review

The existing Site-level model was preserved.

Alignment updates clarify that WebPay, APM, Cashier-Assisted Terminal, Continuity Terminal, operator-assisted payment if allowed, and future channels are children of the Site POS Server and not independent POS systems.

Legacy labels `Cashier POS` and `EC Device / Continuity Terminal` were replaced with v1.3-aligned labels:

- Cashier-Assisted Terminal
- Continuity Terminal

## 6. Fiscal Issuance Before ExitAuthorization Review

The Sales Invoice lifecycle remains aligned to the approved sequence:

1. Central PMS records platform payment finality.
2. Central PMS requests Sales Invoice issuance from the resolved Site POS Server.
3. POS Server issues Sales Invoice and returns fiscal document identity/status.
4. Central PMS records fiscal issuance reference.
5. Central PMS issues ExitAuthorization if eligible.

The lifecycle wording was tightened so ExitAuthorization is not presented as unconditional after fiscal reference recording.

## 7. Fiscal Exception / Pending Exit Review

The existing exception handling already preserved the required posture:

- payment finality is not automatically reversed
- Central PMS does not issue normal ExitAuthorization yet
- the case enters controlled fiscal exception/retry workflow
- customer/operator messaging must distinguish payment received, fiscal pending, and exit not available
- manual release remains supervisor-approved, incident-tagged, and reconciliation-tagged where allowed

The offline fiscal issuance paragraph was updated to state that continuity does not automatically approve offline fiscal issuance and unmanaged offline fiscal issuance is not approved.

## 8. Channel Alignment Review

WebPay alignment:

- WebPay remains customer payment surface.
- WebPay does not declare finality.
- WebPay does not issue fiscal documents.
- WebPay routes fiscal issuance through Central PMS to resolved Site POS Server.

Assisted Payment Terminal alignment:

- Cashier-Assisted Terminal is modeled as a child terminal/channel under Site POS Server.
- It does not independently declare payment finality, issue Sales Invoices independently, approve statutory entitlement, mutate payable basis, issue ExitAuthorization, or open gates.

Continuity alignment:

- Continuity Terminal is a restricted degraded/BCP mode of Assisted Payment Terminal.
- Continuity Terminal is disabled by default.
- Continuity Terminal uses the Site POS Server fiscal authority when activated under approved continuity policy.

Operator-assisted payment alignment:

- Operator-assisted payment remains conditional.
- It is not the Operator Console unless a later approved workflow explicitly defines the operating surface and permission boundary.

## 9. POS/Invoicing and BIR/Fiscal Requirement Alignment

The reviewed design preserves:

- Sales Invoice / SI as the primary parking fiscal output.
- X-read and Z-read support.
- BIR Sales Summary / Annex E-1 support.
- Annex E-2 to E-5 extensibility for Senior Citizen, PWD, NAAC, and Solo Parent.
- Electronic Journal.
- POSLog / ARTS POSLog-aligned export posture where practical and where accepted.
- Reprint controls.
- Void/refund/cancel/return fiscal adjustment posture.
- Fiscal export, retention, audit, and recovery continuity posture.

The entitlement section now explicitly states that Central PMS / Discount workflow owns statutory policy resolution, validation persistence, and payable-basis update.

Diplomat VAT Privilege / VAT Exemption remains modeled as VAT privilege / VAT exemption capability, not an ordinary commercial discount.

## 10. Continuity Alignment

The reviewed design now explicitly aligns with Continuity System Design v1.0:

- Continuity Terminal is disabled by default.
- Continuity does not create unmanaged offline fiscal issuance or unmanaged offline fiscal recovery.
- Fiscal sequence and counter continuity must not be weakened by continuity mode.
- Fiscal issuance failure does not authorize exit automatically.
- Manual release remains a governed exception, not POS Server authority.

## 11. Assisted Payment Terminal Alignment

The design now uses the approved Assisted Payment Terminal terminology:

- Cashier-Assisted Terminal
- Continuity Terminal

The Cashier-Assisted Terminal integration now preserves APT authority boundaries by stating that the terminal does not independently declare payment finality, issue Sales Invoices independently, approve entitlement, mutate payable basis, issue ExitAuthorization, or open gates.

## 12. Operator Console and Management Dashboard Alignment

The design preserves:

- Operator Console may support fiscal exception review/governance, but it is not payment collection, fiscal issuance, or ExitAuthorization authority.
- Management Dashboard may show fiscal status, exception backlog, and reporting visibility, but it is visibility/reporting only.

No Operator Console BRD, Management Dashboard BRD, or companion technical document was modified.

## 13. Vendor PMS Connector / HikCentral Alignment

The POS Server design remains outside Vendor PMS/HikCentral authority:

- Vendor PMS/HCP remains normal raw parking session and tariff authority.
- Vendor connector reports vendor facts and does not create payment finality, fiscal documents, or ExitAuthorization.
- POS Server fiscal issuance occurs after Central PMS payment finality and resolved Site routing.

No Vendor PMS Connector System Design or HikCentral Connector Profile file was modified.

## 14. Runtime Fiscal Numbering and Idempotency Review

The Sales Invoice lifecycle now includes a clearer architecture posture:

- runtime fiscal number allocation is recommended inside the same durable transaction as fiscal document creation
- controlled sequence state must prevent duplicate fiscal numbers
- fiscal number should be returned only after durable commit

The following remain downstream design items:

- idempotency source/key behavior
- semantic request hash
- duplicate request behavior
- timeout behavior
- failed/abandoned issuance treatment
- sequence reservation and gap policy
- BIR/accounting confirmation of sequence behavior

## 15. Offline Fiscal Issuance and Recovery Review

The existing design already included fiscal state integrity, tamper-evidence, backup/restore/failover, and recovery continuity sections.

The alignment update reinforces:

- offline fiscal issuance remains disabled or restricted by default
- continuity does not automatically approve offline fiscal issuance
- unmanaged offline fiscal issuance is not approved
- restore/failover must not resume from lower fiscal counters, lower Grand Total Amount, lower Z-counter, or earlier Sales Invoice sequence
- inability to prove continuity requires supervised recovery and audit record before fiscal issuance resumes

## 16. Open Questions and Deferrals Preserved

The review preserved open downstream items, including:

- MIN/PTU/serial/software/supplier assignment
- taxpayer/Site/branch/Site POS Server/channel fiscal identity assignment
- WebPay fiscal terminal identity
- exact Sales Invoice numbering pattern
- exact adjustment numbering pattern
- sequence gaps, reserved numbers, failed issuance, abandoned issuance, and idempotency
- X-read and Z-read aggregation scope
- VAT/tax treatment
- Diplomat VAT treatment, evidence, wording, reporting, and retention
- digital Sales Invoice URL access policy, expiry, authentication, and audit treatment
- final ARTS POSLog export profile
- tamper-evident state and external anchoring
- recovery procedure after restore/failover/counter continuity failure
- final API endpoint paths, DTOs, status codes, error model, and event model
- final database schema, constraints, indexes, routines, triggers, and migrations
- final engineering implementation, UAT scripts, and runbook procedures

## 17. Changes Made

Changes made in `ExitPass_POS_Server_System_Design_v1.0.md`:

- Added `ExitPass_System_Design_v1.3.md` and approved companion BRDs/technical designs to the reference baseline.
- Replaced legacy `Cashier POS` wording with `Cashier-Assisted Terminal`.
- Replaced legacy `EC Device / Continuity Terminal` wording with `Continuity Terminal`.
- Expanded POS Server non-authority language.
- Added Central PMS / Discount workflow ownership for statutory discount policy resolution and payable-basis update.
- Added POS Server exclusion from continuity activation, manual release approval, and Central PMS fiscal reference recording.
- Clarified that Central PMS issues ExitAuthorization only if eligible.
- Added runtime fiscal number allocation/idempotency posture.
- Clarified continuity/offline fiscal issuance restrictions.
- Clarified operator-assisted payment is not Operator Console unless later approved.

## 18. Issues Found

Issues found and corrected:

- Legacy channel names were still present in the POS Server design.
- The Sales Invoice lifecycle could be read as unconditional ExitAuthorization after fiscal reference recording; it now says `if eligible`.
- The POS Server non-authority boundary did not explicitly list statutory entitlement, payable-basis mutation, continuity activation, manual release approval, or Central PMS fiscal reference recording exclusions.

No contradictions were found requiring modification to approved BRDs, ExitPass System Design v1.3, connector designs, Assisted Payment Terminal System Design, or Continuity System Design.

## 19. Required Fixes, if any

No additional fixes are required in this task.

Downstream work remains required for:

- POS Server API Contract.
- POS Server Database Design.
- BIR/accounting confirmation.
- Security/privacy review.
- Engineering Pack.
- Test/UAT Pack.
- Operations Runbook Pack.

## 20. Recommendation

The POS Server System Design v1.0 is aligned with the approved ExitPass v1.3 architecture baseline for continuation into POS Server API Contract, POS Server Database Design, and downstream implementation planning.

The design remains subject to the retained open questions and downstream finance/accounting/BIR, security/privacy, API, database, engineering, UAT, and runbook decisions.
