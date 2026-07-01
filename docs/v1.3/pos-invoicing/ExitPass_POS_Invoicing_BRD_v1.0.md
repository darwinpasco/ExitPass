# ExitPass POS/Invoicing BRD v1.0

Version: v1.0
Status: Finalized draft for business review
Date: 2026-07-01
Document type: Companion Business Requirements Document
Product scope: ExitPass POS/Invoicing

## 1. Document Control

### 1.1 Version History

| Version | Date | Owner | Summary |
| --- | --- | --- | --- |
| v1.0 | 2026-07-01 | ExitPass documentation stream | Finalized companion BRD for platform-wide POS/Invoicing aligned with ExitPass BRD v1.3, companion BRDs, BIR/POS references, Site-level POS Server fiscal authority, Sales Invoice issuance, fiscal reporting, entitlement/tax treatment, audit, continuity, and reconciliation boundaries. |

### 1.2 Approvals

| Role | Name | Status | Date |
| --- | --- | --- | --- |
| Product Owner | TBD | Pending business review | TBD |
| Finance / Accounting Owner | TBD | Pending business review | TBD |
| Compliance / BIR Advisor | TBD | Pending business review | TBD |
| Operations Owner | TBD | Pending business review | TBD |
| Technical Owner | TBD | Pending downstream design | TBD |

### 1.3 Document Ownership

This BRD is owned by the ExitPass product and documentation stream. It defines business and compliance requirements for POS/Invoicing in the ExitPass v1.3 documentation stream.

This document is not a POS Server System Design, POS Server API Contract, Database Design, Engineering Pack, BIR accreditation submission pack, Hikvision APM-only document, ARTS POSLog technical schema guide, or implementation specification.

## 2. Executive Summary

ExitPass POS/Invoicing is a platform-wide business and compliance capability. It applies to all applicable parking payment channels, including WebPay, AutoPay Machine / APM, Cashier-Assisted Terminal, Continuity Terminal when activated under approved policy, operator-assisted payment if allowed, and future payment channels.

Parking fiscal output for ExitPass v1.3 shall be Sales Invoice. Sales Invoice, SI, and Sales Invoice Number are the primary parking fiscal-output terms for this BRD. Official Receipt / OR terminology shall not be used as the primary parking fiscal output unless a future BIR/accounting decision explicitly changes the fiscal document model.

The fiscal authority model is Site-level. Each Site or parking operation boundary should have one Site-level POS Server. The resolved Site determines which Site POS Server issues the Sales Invoice. Payment channels and terminals are channels/terminals under the resolved Site POS Server and are not independent fiscal authorities.

Central PMS remains payment finality and ExitAuthorization authority. POS Server remains fiscal issuance authority. Fiscal issuance must succeed before Central PMS issues ExitAuthorization unless a separately approved supervisor-controlled exception policy applies.

## 3. Business Context

ExitPass v1.3 clarifies Site Group/Site semantics, centralized WebPay, Vendor PMS connector projection, Assisted Payment Terminal, Continuity, Operator Console, Management Dashboard and Reporting, and platform-wide POS/Invoicing. POS/Invoicing must align with those companion domains without taking over their authority.

The POS/BIR reference materials provide required fiscal context for Sales Invoice, X-read, Z-read, BIR Sales Summary, Electronic Journal, POSLog, reprint, void/refund/cancel/return, counters, reset, accreditation identity, and evaluation controls. Those references do not make the target architecture APM-only or Hikvision-specific. The target business model is platform-wide POS/Invoicing through a resolved Site POS Server.

## 4. Problem Statement

ExitPass requires a consistent fiscal issuance and reporting model across all applicable payment channels. Without a platform-wide POS/Invoicing BRD:

- WebPay, APM, cashier-assisted, continuity, and future channels may diverge in fiscal behavior.
- Sales Invoice issuance could be incorrectly treated as channel-local or APM-only.
- Central PMS payment finality and ExitAuthorization authority could be blurred with POS Server fiscal authority.
- BIR Sales Summary, X-read, Z-read, Annex E reporting, Electronic Journal, POSLog, reprint, reset, and fiscal audit controls may be incomplete or inconsistently scoped.
- Tax, entitlement, discount, VAT privilege, coupon, penalty, lost ticket, overstay, and service-charge treatment may be buried in tariff/payment data rather than explicit fiscal classifications.

## 5. Product Purpose

ExitPass POS/Invoicing shall:

- Provide BIR-authorized POS/Invoicing capability for applicable parking payment channels.
- Issue Sales Invoices through the resolved Site POS Server.
- Preserve Central PMS authority for payment finality and ExitAuthorization.
- Preserve POS Server authority for fiscal issuance, fiscal numbering, fiscal counters, fiscal reports, Electronic Journal, POSLog, fiscal exports, and fiscal audit trail.
- Support BIR-aligned reporting, including X-read, Z-read, BIR Sales Summary / Annex E-1, and extensible Annex E-2 to E-5 reporting.
- Support entitlement and fiscal-treatment categories including Senior Citizen, PWD, NAAC, Solo Parent, and Diplomat VAT Privilege / VAT Exemption.
- Support audit, reprint, void/refund/cancel/return, reset, recovery, continuity exception, and reconciliation controls.

## 6. Product Boundary

POS/Invoicing is:

- A platform-wide fiscal issuance and fiscal reporting capability.
- A Site-level fiscal model.
- A business and compliance requirements domain.
- A consumer of Central PMS payment finality and resolved Site context.
- A producer of Sales Invoice, fiscal references, fiscal reports, Electronic Journal, POSLog, exports, and fiscal audit records.

POS/Invoicing is not:

- AutoPay Machine-only.
- Hikvision-specific.
- A separate independent POS system per payment channel.
- A payment provider.
- Payment finality authority.
- ExitAuthorization authority.
- Gate-control authority.
- Normal tariff authority.
- Statutory discount policy authority.
- Management dashboard or BI implementation.

## 7. Explicit Non-Authority Scope

POS/Invoicing must not:

- Declare payment finality.
- Issue ExitAuthorization.
- Open gates.
- Replace Central PMS authority.
- Replace Vendor PMS normal tariff authority.
- Make APM, WebPay, Cashier-Assisted Terminal, Continuity Terminal, or future channels independent fiscal authorities.
- Treat projection data as financial truth.
- Approve statutory entitlement outside Central PMS / Discount workflow.
- Turn POS Server into payment authority.
- Turn POS Server into gate authority.

## 8. Stakeholders and Users

| Stakeholder / User | Business Interest |
| --- | --- |
| Parking customer | Receives Sales Invoice access and accurate payment/fiscal/exit messaging. |
| WebPay user | Receives digital Sales Invoice access after successful fiscal issuance. |
| APM user | Receives printed or digital Sales Invoice presentation where supported. |
| Cashier / assisted terminal user | Processes assisted payment and fiscal presentation under Site POS Server authority. |
| Continuity operator | Performs restricted continuity-mode fiscal handling only under approved policy. |
| Site supervisor | Reviews fiscal exceptions, reprints, adjustments, continuity events, and manual-release cases where policy allows. |
| Finance / accounting | Owns fiscal treatment, tax treatment, BIR reporting, reconciliation, and accounting signoff. |
| Compliance / BIR advisor | Confirms BIR interpretation, accreditation details, retention, identity, and reporting posture. |
| Operations / reconciliation user | Reviews fiscal exception, settlement, continuity, and post-restoration reconciliation items. |
| Technical owner | Designs POS Server, integrations, API, database, security, and recovery after BRD approval. |

## 9. POS/Invoicing Concept Overview

ExitPass POS/Invoicing sits beside Central PMS payment authority. Central PMS determines verified payment finality and resolves the Site. POS Server issues the Sales Invoice and fiscal records for that resolved Site. Central PMS records the fiscal issuance reference and then issues ExitAuthorization if eligible.

POS Server shall maintain fiscal facts required to reconstruct, audit, export, and report parking fiscal transactions, including Sales Invoice data, fiscal lines, fiscal identity, counters, X-read, Z-read, BIR Sales Summary, Annex E reports, Electronic Journal, POSLog, reprints, adjustments, exports, and fiscal audit events.

## 10. Site-level POS Server Model

Each Site or parking operation boundary should have one Site-level POS Server.

The resolved Site determines:

- Which POS Server issues the Sales Invoice.
- Which fiscal numbering and counters apply.
- Which BIR/fiscal reporting scope applies.
- Which POS Server owns fiscal issuance, Electronic Journal, POSLog, export, retention, and audit records.

Payment channels and terminals are under the Site POS Server. A channel or terminal may present, print, or display fiscal output, but it does not become fiscal authority.

The exact assignment of MIN, PTU, serial number, terminal number, software version, supplier accreditation metadata, and taxpayer/fiscal identity fields across Site, branch, Site POS Server, channel, and terminal remains open for BIR/accounting/accreditation confirmation.

## 11. Payment Channel and Terminal Model

The POS/Invoicing capability shall support:

- WebPay.
- AutoPay Machine / APM.
- Cashier-Assisted Terminal.
- Continuity Terminal when activated under approved policy.
- Operator-assisted payment if allowed.
- Future payment channels.

Each channel/terminal shall provide enough business context for fiscal issuance to be associated with resolved Site, Site POS Server, parking session, payment confirmation, channel, terminal, responsible actor where applicable, and fiscal line basis.

The channel/terminal shall not independently issue fiscal documents, declare payment finality, approve entitlement, or authorize exit.

## 12. Authority Model

| Function | Owner |
| --- | --- |
| Parking session lifecycle in normal mode | Vendor PMS / HCP |
| Normal tariff computation | Vendor PMS / HCP |
| Session projection and control state | Central PMS |
| TariffSnapshot / payable basis | Central PMS |
| PaymentAttempt | Central PMS |
| PaymentConfirmation / payment finality | Central PMS |
| Payment provider interaction | Payment Orchestrator or approved payment channel integration |
| Statutory discount policy resolution | Central PMS / Discount workflow |
| Statutory validation record | Central PMS / Discount workflow |
| Payable-basis update after discount | Central PMS with Vendor PMS or approved degraded-mode tariff basis |
| Fiscal treatment and Sales Invoice issuance | Resolved Site POS Server |
| Fiscal numbering / counters / X-read / Z-read / BIR Sales Summary / EJ / POSLog | Resolved Site POS Server |
| Fiscal issuance reference recording | Central PMS |
| ExitAuthorization | Central PMS |
| Gate/exit execution | Gate/exit system consuming Central PMS authorization |
| Cashier-facing payment workflow | Assisted Payment Terminal |
| Continuity Terminal UI | Assisted Payment Terminal in Continuity Terminal mode |
| Supervisor/compliance review | Operator Console / approved operations workflow |
| Reporting visibility | Management Dashboard and Reporting |
| Reconciliation and post-restoration review | Operations / Reconciliation workflow |

## 13. Relationship to ExitPass BRD v1.3

ExitPass BRD v1.3 anchors the platform-wide POS/Invoicing requirement, Site-level POS Server routing, fiscal issuance before ExitAuthorization, and preserved authority model. This BRD expands those requirements into POS/Invoicing-specific business and compliance requirements.

ExitPass BRD v1.3 remains the core business baseline. This BRD does not change Central PMS, Vendor PMS, Payment Orchestrator, WebPay, POS Server, or gate authority boundaries.

## 14. Relationship to Assisted Payment Terminal

Assisted Payment Terminal is the payment-capable terminal app family. It supports Cashier-Assisted Terminal mode and Continuity Terminal mode.

Cashier-Assisted Terminal may capture statutory discount validation inputs during assisted payment, but Central PMS / Discount workflow remains the authority for validation persistence, policy resolution, and payable-basis update.

Continuity Terminal is restricted degraded/BCP mode and is disabled by default. It may use POS/Invoicing only under approved continuity policy and shall route fiscal issuance through the resolved Site POS Server or an approved continuity variant.

Assisted Payment Terminal shall not declare payment finality, issue Sales Invoices independently, or issue ExitAuthorization.

## 15. Relationship to Continuity

ExitPass Continuity is the controlled degraded-operation capability. Continuity does not create a silent alternate fiscal mode.

Continuity-mode fiscal handling shall be explicit, approved, audited, incident-tagged, reconciliation-tagged, and subject to post-restoration review. Continuity does not automatically permit offline fiscal issuance.

If fiscal issuance fails or times out during continuity or normal operation, payment finality is not automatically reversed and ExitAuthorization is not issued yet unless a separately approved exception/manual-release policy applies.

## 16. Relationship to Operator Console

Operator Console is a non-payment governance and review module.

Operator Console may review statutory discount evidence, fiscal exceptions, continuity activation, manual release governance, audit trails, and post-restoration review records. It shall not collect payment, issue Sales Invoices, mutate fiscal records, declare payment finality, or issue ExitAuthorization.

## 17. Relationship to Management Dashboard and Reporting

Management Dashboard and Reporting is visibility/reporting only. Operational visibility is not financial truth.

Fiscal dashboards shall reconcile to POS Server fiscal records and Central PMS fiscal issuance references. Management Dashboard shall not issue fiscal documents, mutate fiscal records, declare payment finality, or issue ExitAuthorization.

## 18. Business Process Overview

### 18.1 Standard Payment-to-Exit Choreography

1. Central PMS receives verified payment finality.
2. Central PMS requests Sales Invoice issuance from the resolved Site POS Server.
3. POS Server successfully issues the Sales Invoice and returns fiscal document identity/status.
4. Central PMS records the fiscal issuance reference.
5. Central PMS issues ExitAuthorization if eligible.

### 18.2 Fiscal Issuance Failure or Timeout

If fiscal issuance fails or times out:

- Payment finality is not automatically reversed.
- ExitAuthorization is not issued yet.
- The case enters a controlled fiscal issuance exception/retry workflow.
- Customer/operator messaging must state that payment was received but fiscal issuance or exit authorization is pending.
- Manual release, if allowed, must be supervisor-approved, incident-tagged, audit-tagged, and reconciliation-tagged.

## 19. Functional Requirements

| ID | Requirement |
| --- | --- |
| POS-FR-001 | ExitPass shall provide BIR-authorized POS/Invoicing capability for all applicable parking payment channels. |
| POS-FR-002 | The POS/Invoicing capability shall be platform-wide and shall not be APM-only or Hikvision-specific. |
| POS-FR-003 | The system shall use Sales Invoice as the primary parking fiscal output. |
| POS-FR-004 | The resolved Site shall determine the Site POS Server for fiscal issuance. |
| POS-FR-005 | Payment channels and terminals shall be modeled under the resolved Site POS Server. |
| POS-FR-006 | Central PMS shall remain payment finality authority. |
| POS-FR-007 | POS Server shall remain fiscal issuance authority. |
| POS-FR-008 | POS Server shall not issue ExitAuthorization. |
| POS-FR-009 | Fiscal issuance shall succeed before Central PMS issues ExitAuthorization under normal flow. |
| POS-FR-010 | POS Server shall own fiscal numbering, counters, X-read, Z-read, BIR Sales Summary, EJ, POSLog, reprint controls, adjustment controls, fiscal retention, and fiscal export. |
| POS-FR-011 | Fiscal reports shall reconcile to Sales Invoice sequence, fiscal counters, EJ, POSLog, audit records, and Central PMS fiscal issuance references. |
| POS-FR-012 | The system shall support fiscal treatment for VATable, VAT-exempt, zero-rated, non-VAT, statutory discount, VAT privilege/exemption, coupon, penalty, lost ticket fee, overstay charge, service charge, and other fiscal adjustment lines. |
| POS-FR-013 | POS/Invoicing shall support Senior Citizen and PWD as immediate statutory entitlement workflows. |
| POS-FR-014 | POS/Invoicing shall support NAAC and Solo Parent as future-supported statutory entitlement categories. |
| POS-FR-015 | POS/Invoicing shall support Diplomat VAT Privilege / VAT Exemption as an active VAT privilege/exemption category, not an ordinary commercial discount. |
| POS-FR-016 | POS Server shall support controlled reprints for Sales Invoice, X-read, Z-read, and Electronic Journal where applicable. |
| POS-FR-017 | POS Server shall support controlled void, refund, cancel, return, and related fiscal adjustment workflows as required by BIR/accounting. |
| POS-FR-018 | Fiscal state shall be tamper-evident, append-only, and recoverable without silent rollback. |
| POS-FR-019 | POS Server shall return only the digital Sales Invoice URL for QR-capable channels; channel/terminal presentation shall generate or render the QR code. |
| POS-FR-020 | POS/Invoicing shall support audited fiscal exports, reporting, retention, and reconciliation. |

## 20. Channel-Specific Requirements

### 20.1 WebPay

WebPay is the centralized customer payment surface with site-specific/payment-scope URLs. WebPay shall route fiscal issuance through the resolved Site POS Server. WebPay shall not declare payment finality, act as fiscal authority, or issue ExitAuthorization.

### 20.2 AutoPay Machine / APM

APM shall be a channel/terminal under the resolved Site POS Server. APM may print or display the Site POS Server-issued Sales Invoice where supported. APM shall not become an independent fiscal authority, payment finality authority, or exit authority.

### 20.3 Cashier-Assisted Terminal

Cashier-Assisted Terminal shall route fiscal issuance through the resolved Site POS Server. It may capture statutory discount validation inputs as part of assisted payment, but it shall not independently approve entitlement, mutate payable basis, issue fiscal documents independently, declare payment finality, or issue ExitAuthorization.

### 20.4 Continuity Terminal

Continuity Terminal shall support fiscal handling only under approved degraded/BCP policy. Offline fiscal issuance remains restricted/open until BIR/accounting and POS Server design approve the sequence/counter model. Continuity Terminal shall not silently replace the Site POS Server fiscal model.

### 20.5 Operator-assisted Payment, If Allowed

Operator-assisted payment, if allowed, shall route fiscal issuance through the resolved Site POS Server and shall preserve Central PMS payment finality and ExitAuthorization authority. Operator Console itself remains non-payment and non-fiscal.

### 20.6 Future Payment Channels

Future payment channels shall register as channels/terminals under the resolved Site POS Server and shall preserve the same authority boundaries, fiscal routing, audit, privacy, and reporting controls.

## 21. Sales Invoice Requirements

POS Server shall issue Sales Invoice as the primary fiscal output for successful parking payments.

Sales Invoice shall support:

- Sales Invoice Number.
- Resolved Site and Site POS Server identity.
- Payment/session/fiscal reference correlation.
- Channel/terminal identity where applicable.
- Fiscal line basis.
- VAT/tax/discount/entitlement/coupon/adjustment classification.
- Printed and digital presentation where supported.
- Required BIR/accreditation identity details once confirmed.

Sales Invoice and fiscal outputs must support taxpayer/fiscal identity details required by BIR/accreditation materials, including where applicable:

- Registered name.
- Trade name.
- Business address.
- VAT REG TIN / NON-VAT REG TIN.
- MIN.
- Serial number.
- PTU number and date issued.
- Supplier name.
- Supplier address.
- Supplier TIN.
- Accreditation number.
- Accreditation date issued.
- Accreditation valid until.

Exact assignment across taxpayer, Site, branch, Site POS Server, channel, and terminal remains a downstream BIR/accounting/accreditation confirmation item.

## 22. Fiscal Issuance Before ExitAuthorization

Central PMS shall request Sales Invoice issuance only after verified payment finality.

POS Server shall issue the Sales Invoice and return fiscal document identity/status to Central PMS. Central PMS shall record the fiscal issuance reference before issuing ExitAuthorization if eligible.

If fiscal issuance fails or times out, Central PMS shall not issue normal ExitAuthorization yet. The case shall enter controlled exception/retry workflow.

## 23. X-read, Z-read, Reset Counter, and Grand Total Requirements

POS Server shall support X-read and Z-read for BIR/accounting-approved scopes.

The following counter rules apply:

- Reset counter starts from zero.
- Reset counter increments by one for each fiscal reset.
- Z-counter advances per Z-reading / fiscal day close.
- Reset counter and Z-counter are different controls.
- POS Server must save the last Grand Total Amount and reset counter for audit reference.
- Reset activity must preserve previous Grand Total Amount, previous reset counter, reset timestamp, reset reason, approving user, and recovery/reference notes.

Exact X-read and Z-read aggregation scope remains open for BIR/accounting confirmation. Candidate scopes include Site-level, terminal-level, cashier/session-level, or combined Site + terminal + cashier/session model.

## 24. BIR Sales Summary and Annex E Reporting Requirements

BIR Sales Summary / Annex E-1 shall be first-class required fiscal reporting, not optional analytics.

BIR Sales Summary / Annex E-1 must reconcile to:

- Sales Invoice sequence.
- Z-counter.
- Reset counter.
- VAT and deductions.
- Fiscal totals.
- Supporting fiscal records.
- Electronic Journal.
- POSLog.
- Fiscal audit records.

Annex E-2 to E-5 support must remain in the extensible model for Senior Citizen, PWD, NAAC, and Solo Parent reporting where applicable. Annex E-2 to E-5 shall not be permanently excluded solely because an APM-specific gap analysis treated them as not applicable to that APM scope.

## 25. Entitlement, Discount, and VAT Privilege Requirements

POS/Invoicing shall support an extensible entitlement and fiscal treatment model.

Immediate statutory entitlement workflows:

- Senior Citizen.
- PWD.

Future-supported statutory entitlement categories:

- NAAC.
- Solo Parent.

Active VAT privilege / exemption category:

- Diplomat VAT Privilege / VAT Exemption.

Diplomat VAT Privilege / VAT Exemption shall not be modeled as an ordinary commercial discount. It shall be modeled as a VAT privilege / VAT exemption entitlement based on BIR Revenue Memorandum Order No. 10-2019, with exact implementation details open for accounting/BIR confirmation.

Central PMS / Discount workflow remains the authority for statutory policy resolution, validation persistence, and payable-basis update. POS Server owns fiscal treatment on the Sales Invoice and fiscal reports.

ExitPass shall not apply a generic nationwide parking free/discount rule blindly. Local parking statutory benefits must be resolved by Site jurisdiction and active policy. Each ordinance or policy should be represented separately at the business-rule level by entitlement type, jurisdiction, residency scope, benefit type, exclusions, verification status, effective date, and source review status. Production application requires official ordinance/policy review.

## 26. VAT, Tax Treatment, and Fiscal Line Classification Requirements

At business level, POS/Invoicing shall support fiscal classification for:

- VATable sales.
- VAT-exempt sales.
- Zero-rated sales.
- Non-VAT sales.
- Statutory discounts.
- VAT privileges / VAT exemptions.
- Coupons.
- Penalties.
- Lost ticket fees.
- Overstay charges.
- Service charges.
- Other fiscal adjustments.

Exact tax treatment by Site, taxpayer, transaction type, entitlement type, and line item remains a finance/accounting/BIR confirmation item.

Fiscal tax treatment shall not be buried only inside tariff snapshots. Fiscal line classification shall remain visible to POS Server fiscal records, reports, audit, and exports.

## 27. Void, Refund, Cancel, Return, and Reprint Requirements

POS Server shall support controlled fiscal actions for void, refund, cancel, return, and related adjustment documents as required by BIR/accounting.

These actions shall:

- Preserve the original Sales Invoice reference.
- Preserve Central PMS payment finality authority for payment reversal/refund outcomes.
- Be role-restricted.
- Be reason-coded.
- Be audited.
- Reconcile to fiscal reports and exports.

The system shall support reprint of:

- Sales Invoice.
- X-read.
- Z-read.
- Electronic Journal, where applicable.

Reprints must show `REPRINT` and `DATE / TIME REPRINTED`. All reprint activity must be logged.

## 28. Fiscal Audit, Electronic Journal, POSLog, Export, and Retention Requirements

POS Server shall maintain fiscal audit and Electronic Journal records sufficient to reconstruct fiscal documents, fiscal reports, counters, exports, adjustments, and privileged actions.

Audit/evidence requirements shall cover:

- Fiscal issuance.
- Failed/timeout fiscal issuance.
- Reprints.
- Void/refund/cancel/return.
- X-read.
- Z-read.
- BIR Sales Summary.
- Fiscal exports.
- Reset/recovery.
- Taxpayer/fiscal identity changes.
- Terminal/channel changes.
- Privileged actions.
- Statutory discount / entitlement / VAT privilege evidence.
- Continuity-mode fiscal exceptions.
- Manual release under fiscal exception.

POS Server should support POSLog export structure aligned to ARTS POSLog 6.x where practical and where BIR/local requirements allow. ARTS supports structured transaction/export modeling, transaction identity, line sequences, tender/tax/discount/totals, statuses, and extension points. ARTS does not override Philippine BIR fiscal document/report requirements. The exact submitted schema/profile and export packaging remain open for BIR/accreditation and technical design.

## 29. Digital Sales Invoice URL and QR Presentation Requirements

POS Server returns only the digital Sales Invoice URL.

The channel or terminal converts the URL into a QR code when QR presentation is supported. QR generation, display, or printing is a channel/terminal presentation responsibility.

POS Server remains fiscal issuer. QR presentation does not make the channel/terminal fiscal authority.

Digital Sales Invoice URL token, access, expiry, authentication, privacy, and anti-tampering controls remain open for POS Server System Design and compliance confirmation.

## 30. Exception and Failure Handling

If Sales Invoice issuance fails after verified payment finality:

- Central PMS shall not issue normal ExitAuthorization yet.
- Payment finality shall not be automatically reversed.
- The fiscal issuance exception shall be retryable or escalated according to approved policy.
- Customer/operator messaging shall state that payment was received but fiscal issuance or exit authorization is pending.
- Operator Console may support governance/review, but shall not issue Sales Invoice or ExitAuthorization.

If manual release is allowed under fiscal exception, it must be supervisor-approved, incident-tagged, audit-tagged, and reconciliation-tagged.

## 31. Business Continuity and Degraded Operation

Offline fiscal issuance remains restricted/open until BIR/accounting/POS Server design approves the sequence/counter model. Unmanaged offline fiscal issuance is not approved. Continuity does not automatically permit offline fiscal issuance.

Fiscal state must be tamper-evident, append-only, and recoverable without silent rollback. Restore/failover must not resume from lower fiscal counters, lower Grand Total Amount, lower Z-counter, or earlier Sales Invoice sequence than the last externally anchored fiscal state.

Inability to prove continuity requires supervised recovery and a recovery audit record before fiscal issuance resumes. Implementation details are deferred to POS Server System Design.

## 32. Security, RBAC, and Segregation of Duties

The system shall enforce segregation of duties across:

- Payment finality.
- Fiscal issuance.
- Fiscal adjustment.
- Reprint.
- Reset.
- Recovery.
- Export.
- ExitAuthorization.
- Manual release.
- Tax/fiscal configuration.

Privileged fiscal actions shall require appropriate authorization and audit. POS Server shall not be able to issue ExitAuthorization. Payment channels shall not be able to bypass POS Server fiscal issuance.

## 33. Data Privacy and Evidence Handling

Evidence and personal data required for Senior Citizen, PWD, NAAC, Solo Parent, and Diplomat VAT Privilege / VAT Exemption shall be handled according to approved privacy, retention, access, and audit policy.

Diplomat VAT Privilege / VAT Exemption evidence requirements remain open for compliance/accounting confirmation. Candidate evidence may include BIR-issued VAT Certificate, VAT Identification Card, DFA/BIR-issued documentation, or other approved supporting evidence, pending final confirmation.

The Sales Invoice URL shall not allow unauthorized modification of the Sales Invoice and shall not expose unnecessary sensitive data.

## 34. Reporting and Reconciliation

POS Server fiscal reports shall reconcile with Sales Invoice records, fiscal lines, counters, Electronic Journal, POSLog, and audit records.

Central PMS shall retain payment and ExitAuthorization authority records that reconcile to POS Server fiscal issuance references.

Management Dashboard and Reporting may consume fiscal summaries or references where authorized, but it is not fiscal authority and must reconcile fiscal dashboards to POS Server fiscal records and Central PMS fiscal references.

## 35. Non-Functional Requirements

| Category | Requirement |
| --- | --- |
| Integrity | Fiscal state shall be tamper-evident, append-only, and protected from silent rollback. |
| Traceability | The system shall preserve traceability from payment finality to Sales Invoice to fiscal reference to ExitAuthorization where applicable. |
| Availability | POS/Invoicing should meet later-defined operating availability targets by Site and channel. |
| Recoverability | Recovery/failover shall preserve fiscal counters, Grand Total Amount, Sales Invoice sequence, and audit evidence. |
| Auditability | Fiscal issuance, failure, reprint, adjustment, reset, export, and privileged actions shall be auditable. |
| Privacy | Sensitive entitlement and VAT privilege evidence shall be minimized, access-controlled, and retained only under approved policy. |
| Reconciliation | Fiscal outputs, reports, exports, EJ, POSLog, and Central PMS references shall reconcile. |

## 36. Assumptions

- Sales Invoice is the primary parking fiscal document for v1.3.
- Site-level POS Server is the target fiscal authority model.
- Central PMS remains payment finality and ExitAuthorization authority.
- POS Server remains fiscal issuance authority.
- BIR/accounting confirmation will be required before implementation of taxpayer identity, MIN/PTU/serial/software assignment, tax treatment, and accreditation details.
- POS Server System Design will define technical architecture after this BRD is approved.

## 37. Constraints

- This BRD shall not define endpoint paths, DTOs, database tables, SQL routines, event payloads, or deployment scripts.
- This BRD shall not approve unmanaged offline fiscal issuance.
- This BRD shall not treat APM, WebPay, Cashier-Assisted Terminal, Continuity Terminal, or future channels as independent fiscal authorities.
- This BRD shall not treat projection data as financial truth.
- This BRD shall not override Philippine BIR fiscal document/report requirements with ARTS POSLog references.

## 38. Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Fiscal issuance is treated as APM-only | WebPay, cashier, continuity, and future channels diverge. | Preserve platform-wide Site POS Server model. |
| Sales Invoice is confused with another fiscal output | Incorrect fiscal document, reporting, or customer output. | Use Sales Invoice/SI terminology as primary parking fiscal output. |
| ExitAuthorization is issued before fiscal issuance | Paid exit may occur without completed fiscal issuance. | Require fiscal issuance reference before normal ExitAuthorization. |
| POS Server is treated as payment or exit authority | Authority model violation. | Explicitly preserve Central PMS payment finality and ExitAuthorization authority. |
| Diplomat VAT Privilege is treated as commercial discount | VAT exemption may be reported incorrectly. | Model as VAT privilege/exemption entitlement, with details open for BIR/accounting. |
| Local ordinance rules are applied generically | Wrong statutory benefit by Site jurisdiction. | Use Site jurisdiction and active policy registry, with official ordinance review before production application. |
| Offline issuance creates duplicate sequence or counter gaps | Fiscal compliance and audit risk. | Keep offline issuance restricted until approved sequence/counter model exists. |
| Restore resumes from stale fiscal state | Fiscal rollback or duplicate fiscal sequence. | Require external anchoring, supervised recovery, and recovery audit record. |

## 39. Open Questions

| ID | Open Question |
| --- | --- |
| POS-OQ-001 | What is the exact MIN/PTU/serial/software/supplier assignment across Site POS Server, APM terminal, Cashier-Assisted Terminal, Continuity Terminal, WebPay channel, and operator-assisted channel? |
| POS-OQ-002 | What is the exact taxpayer/Site/branch/Site POS Server/channel fiscal identity assignment? |
| POS-OQ-003 | What is the WebPay fiscal terminal identity, if any, for BIR/accreditation purposes? |
| POS-OQ-004 | What is the exact Sales Invoice numbering pattern? |
| POS-OQ-005 | What is the exact adjustment document numbering pattern? |
| POS-OQ-006 | How are sequence gaps, reserved numbers, failed issuance, and abandoned issuance handled? |
| POS-OQ-007 | What is the exact X-read and Z-read aggregation scope? |
| POS-OQ-008 | What exact VAT/tax treatment applies by Site, taxpayer, transaction type, entitlement type, and line item? |
| POS-OQ-009 | What exact Diplomat VAT treatment, evidence, wording, reporting, and retention rules apply? |
| POS-OQ-010 | What is the digital SI URL token/access/expiry/authentication model? |
| POS-OQ-011 | What is the final ARTS POSLog export profile/schema mapping? |
| POS-OQ-012 | What is the final JSON schema versioning and validation strategy? |
| POS-OQ-013 | What is the final accreditation sample package? |
| POS-OQ-014 | What tamper-evident anchoring/recovery mechanism is required? |
| POS-OQ-015 | What are the final endpoint names? Deferred to API Contract. |
| POS-OQ-016 | What are the final DTO boundaries? Deferred to API Contract. |
| POS-OQ-017 | What are the final database tables/columns? Deferred to Database Design / Database Delta. |
| POS-OQ-018 | What are the final event payloads? Deferred to System Design / Engineering Pack. |
| POS-OQ-019 | What is the final permission matrix/RBAC? |

## 40. Acceptance Criteria

| ID | Acceptance Criterion |
| --- | --- |
| POS-AC-001 | POS/Invoicing applies across WebPay, APM, Cashier-Assisted Terminal, Continuity Terminal where approved, operator-assisted payment if allowed, and future payment channels. |
| POS-AC-002 | Sales Invoice is the primary parking fiscal output. |
| POS-AC-003 | Each Site or parking operation boundary uses a Site-level POS Server model. |
| POS-AC-004 | The resolved Site determines fiscal issuance and fiscal reporting. |
| POS-AC-005 | Payment channels and terminals are not independent fiscal authorities. |
| POS-AC-006 | Central PMS remains payment finality authority. |
| POS-AC-007 | POS Server remains fiscal issuance authority. |
| POS-AC-008 | POS Server does not issue ExitAuthorization. |
| POS-AC-009 | Fiscal issuance succeeds before Central PMS issues normal ExitAuthorization. |
| POS-AC-010 | Fiscal issuance failure prevents normal ExitAuthorization and starts controlled exception/retry workflow. |
| POS-AC-011 | Senior Citizen and PWD are supported as immediate statutory entitlement workflows. |
| POS-AC-012 | NAAC and Solo Parent remain in the extensible future-supported entitlement model. |
| POS-AC-013 | Diplomat VAT Privilege / VAT Exemption is represented as active VAT privilege/exemption, not commercial discount. |
| POS-AC-014 | BIR Sales Summary / Annex E-1 is first-class required fiscal reporting. |
| POS-AC-015 | Annex E-2 to E-5 remain in the extensible model where applicable. |
| POS-AC-016 | Reset counter and Z-counter are separate controls. |
| POS-AC-017 | POS Server preserves Grand Total Amount and reset audit references. |
| POS-AC-018 | Reprints show REPRINT and DATE / TIME REPRINTED and are audited. |
| POS-AC-019 | POS Server returns only the digital Sales Invoice URL; QR presentation is channel/terminal responsibility. |
| POS-AC-020 | Offline fiscal issuance remains restricted until approved. |
| POS-AC-021 | ARTS POSLog is captured as supporting structured export posture, not fiscal authority. |
| POS-AC-022 | Fiscal outputs reconcile across Sales Invoice, X-read, Z-read, BIR Sales Summary, Annex E, EJ, POSLog, exports, audit, and Central PMS fiscal references. |

## 41. Requirements Traceability Matrix

| Business Need | Requirement / Section | Source / Driver |
| --- | --- | --- |
| Platform-wide POS/Invoicing | Sections 2, 5, 11, 20, POS-AC-001 | ExitPass BRD v1.3; planning decisions |
| Sales Invoice primary output | Sections 2, 21, POS-AC-002 | POS/BIR references; v1.3 product decision |
| Site-level POS Server | Sections 10, 12, POS-AC-003 to POS-AC-005 | ExitPass BRD v1.3; companion BRDs |
| Fiscal issuance before ExitAuthorization | Sections 18, 22, 30, POS-AC-009 to POS-AC-010 | ExitPass BRD v1.3; Continuity BRD |
| Channel alignment | Sections 14 to 17, 20 | Assisted Payment Terminal, Operator Console, Continuity, Management Dashboard BRDs |
| Entitlement and VAT privilege | Sections 25, 26, 33, POS-AC-011 to POS-AC-013 | Statutory discount references; RMO No. 10-2019 |
| Local ordinance policy registry | Section 38; POS-OQ-008 to POS-OQ-009 | Philippine parking statutory discount local ordinance reference |
| Fiscal reporting | Sections 23, 24, 28, 34 | BIR POS references; RMO 24-2023 Annex references |
| Reprint and adjustment controls | Section 27 | BIR/POS references |
| ARTS POSLog posture | Section 28, POS-AC-021 | ARTS POSLog v6.0 references |
| Digital SI URL and QR presentation | Section 29, POS-AC-019 | v1.3 POS/Invoicing decision |
| Open implementation questions | Section 39 | Deferred System Design, API, Database, Engineering Pack |

## 42. Appendix A: Glossary

| Term | Definition |
| --- | --- |
| Sales Invoice | Primary fiscal document for ExitPass v1.3 parking payment output. |
| Sales Invoice Number | Fiscal identifier assigned to an issued Sales Invoice. |
| Site POS Server | Site-level fiscal authority that issues Sales Invoices and owns fiscal reports, counters, EJ, POSLog, audit, retention, and export for the resolved Site. |
| Central PMS | ExitPass platform authority for session projection/control state, payment finality, fiscal reference recording, and ExitAuthorization. |
| Fiscal issuance reference | Central PMS record linking payment/session context to POS Server-issued fiscal document identity/status. |
| X-read | Interim fiscal reading/report that does not close the fiscal day unless BIR/accounting confirms otherwise. |
| Z-read | Fiscal day close report that advances the Z-counter. |
| Reset counter | Fiscal reset counter that starts at zero and increments only on fiscal reset. |
| Z-counter | Counter that advances per Z-reading / fiscal day close. |
| Grand Total Amount | Fiscal accumulated total requiring preservation and audit continuity. |
| Electronic Journal | Fiscal record used to reconstruct fiscal documents and reports. |
| POSLog | Structured fiscal transaction log/export expected to reconcile with EJ and fiscal reports. |
| Diplomat VAT Privilege / VAT Exemption | Active VAT privilege / VAT exemption entitlement category based on BIR RMO No. 10-2019, not an ordinary commercial discount. |
| Continuity Terminal | Restricted degraded/BCP mode of Assisted Payment Terminal. |

## 43. Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| APM | AutoPay Machine |
| ARTS | Association for Retail Technology Standards |
| BCP | Business Continuity Plan |
| BIR | Bureau of Internal Revenue |
| BRD | Business Requirements Document |
| EJ | Electronic Journal |
| HCP | HikCentral Professional |
| MIN | Machine Identification Number |
| NAAC | National Athletes and Coaches |
| PMS | Parking Management System |
| POS | Point of Sale |
| POSLog | Point-of-Sale Log |
| PTU | Permit to Use |
| PWD | Person with Disability |
| QR | Quick Response code |
| RBAC | Role-Based Access Control |
| RMO | Revenue Memorandum Order |
| SI | Sales Invoice |
| VAT | Value-Added Tax |

## 44. Appendix C: Diagrams

Existing POS/Invoicing BRD-level diagrams were verified as having both PlantUML source and JPEG exports under `docs/v1.3/pos-invoicing/diagrams/`.

| Diagram ID | Diagram | PlantUML Source |
| --- | --- | --- |
| D-01 | [POS/Invoicing Context Diagram](diagrams/ExitPass_POS_Invoicing_Context_Diagram.jpg) | [ExitPass_POS_Invoicing_Context_Diagram.puml](diagrams/ExitPass_POS_Invoicing_Context_Diagram.puml) |
| D-02 | [Site-level POS Server Model](diagrams/ExitPass_Site_Level_POS_Server_Model.jpg) | [ExitPass_Site_Level_POS_Server_Model.puml](diagrams/ExitPass_Site_Level_POS_Server_Model.puml) |
| D-03 | [Payment-to-Exit Fiscal Sequence](diagrams/ExitPass_Payment_to_Exit_Fiscal_Sequence.jpg) | [ExitPass_Payment_to_Exit_Fiscal_Sequence.puml](diagrams/ExitPass_Payment_to_Exit_Fiscal_Sequence.puml) |
| D-04 | [Channel / Terminal Fiscal Routing](diagrams/ExitPass_Channel_Terminal_Fiscal_Routing.jpg) | [ExitPass_Channel_Terminal_Fiscal_Routing.puml](diagrams/ExitPass_Channel_Terminal_Fiscal_Routing.puml) |
| D-05 | [Fiscal Output and Reporting Model](diagrams/ExitPass_Fiscal_Output_Reporting_Model.jpg) | [ExitPass_Fiscal_Output_Reporting_Model.puml](diagrams/ExitPass_Fiscal_Output_Reporting_Model.puml) |
| D-06 | [Fiscal Issuance Failure Exception Flow](diagrams/ExitPass_Fiscal_Issuance_Failure_Exception_Flow.jpg) | [ExitPass_Fiscal_Issuance_Failure_Exception_Flow.puml](diagrams/ExitPass_Fiscal_Issuance_Failure_Exception_Flow.puml) |
