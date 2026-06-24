# ExitPass POS/Invoicing Decision Log

Version: v1.3 decision update planning artifact
Status: Draft for planning only
Generated: 2026-06-25

## Decisions

| ID | Decision | Status | Source / rationale | Impact |
| --- | --- | --- | --- | --- |
| POS-D001 | Treat POS/Invoicing as a platform-wide ExitPass capability, not only an AutoPay Machine feature. | Accepted | User architecture direction; BIR fiscal outputs apply across payment channels. | BRD must model WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future channels under POS/Invoicing. |
| POS-D002 | Use one Site-level POS Server per Site or parking operation boundary. | Accepted | BIR recommended the Site-level POS Server model; v1.2 already has site/site-group boundaries. | The resolved Site determines the fiscal POS Server. Fiscal numbering, counters, reports, and audit belong to that Site POS Server. |
| POS-D003 | Model payment channels and terminals as children of the Site-level POS Server. | Accepted | BIR-recommended Site-level model; user decision. | WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future payment channels must not be modeled as independent POS systems. |
| POS-D004 | Parking payment fiscal output for ExitPass v1.3 shall be Sales Invoice. | Decided | BIR guidance and Hikvision examiner gap analysis requiring shift from Official Receipt wording to Sales Invoice for parking payment output. | POS-Q001 and POS-P001 are closed. Other document types can remain design considerations for adjustments or other transaction types. |
| POS-D005 | Central PMS remains authority for payment finality. | Accepted | User architecture direction; v1.2 DDL/services define payment attempts, confirmations, provider outcomes, and finality paths. | POS Server consumes confirmed payment context; it must not independently mark a payment as final. |
| POS-D006 | Central PMS remains authority for ExitAuthorization. | Accepted | User architecture direction; v1.2 DDL/services define `core.exit_authorizations` issue/consume behavior. | POS Server, Payment Orchestrator, WebPay, APM, and cashier channels must not issue or bypass ExitAuthorization. |
| POS-D007 | POS Server owns fiscal issuance and fiscal reports. | Accepted | BIR blueprint, Annex E, Annex G, Hikvision gap/checklist, and Site-level POS Server decision. | POS Server owns Sales Invoice issuance, numbering, reset counter, X-read, Z-read, BIR sales summary, adjustment fiscal controls, cashier/session accountability, audit trail, EJ, POSLog, and fiscal reporting. |
| POS-D008 | Fiscal issuance must succeed before Central PMS issues ExitAuthorization. | Decided | User decision to require fiscal issuance before exit release while preserving Central PMS finality authority. | Final choreography: verified payment finality, POS Sales Invoice issuance, Central PMS fiscal reference recording, then ExitAuthorization issuance. |
| POS-D009 | Failed or timed-out fiscal issuance after payment finality enters controlled exception/retry handling. | Decided | User decision. | Payment finality is not automatically reversed. ExitAuthorization is not issued until fiscal issuance succeeds or a supervisor-approved manual release path is followed. |
| POS-D010 | Printed fiscal outputs should be simplified, while complete detail remains in digital records. | Accepted | Hikvision gap analysis says examiner found printed receipt/X/Z too long and recommends Annex D-style printouts plus detailed JSON/EJ/POSLog/PDF/backend exports. | BRD should separate printed layout requirements from canonical fiscal payload requirements. |
| POS-D011 | BIR Sales Summary is a required first-class report. | Accepted | Annex E-1; Hikvision gap says Annex F Item 45(d)(1) requires BIR Sales Summary. | Do not treat sales summary as optional analytics. It must reconcile to SI sequence, Z-counter, reset counter, VAT and deductions. |
| POS-D012 | Reprint operations are controlled fiscal actions. | Accepted | Hikvision gap analysis requires Sales Invoice, X-Read, Z-Read, and EJ reprints with label and logging. | Reprints must not alter original fiscal documents. Every reprint requires audit logging and clear print labeling. |
| POS-D013 | Reset counter starts from zero and increments only when a fiscal reset event occurs. | Decided | User decision; Annex E-1 and BIR sources require reset counter reporting. | Reset counter must not be confused with Z-counter. Reset events must preserve previous grand total, previous reset counter, timestamp, reason, approving user, and recovery/reference notes. |
| POS-D014 | Z-counter is separate from reset counter and advances per Z-reading / fiscal day close. | Decided | User decision; Annex D-2, Annex E-1, Annex G, and Hikvision sources require Z-reading/Z-counter behavior. | Daily fiscal close must not increment reset counter. Reset events must not be treated as Z-close events. |
| POS-D015 | POS Server must preserve last Grand Total Amount and reset counter as audit references. | Decided | User decision and BIR grand accumulated sales requirements. | Reset, restore, failover, and recovery workflows must prove continuity of Grand Total Amount, reset counter, Z-counter, latest Sales Invoice number, latest EJ hash, and last fiscal event timestamp. |
| POS-D016 | POS Server fiscal line model must explicitly support fiscal classifications beyond tariff snapshots. | Accepted | Annex E, Sales Invoice layout, EJ, POSLog, and audit require VAT/tax/discount reporting detail. | BRD may state required classification capability, while exact tax treatment remains finance/accounting configuration and confirmation. |
| POS-D017 | Senior Citizen and PWD are immediate operational entitlement workflows. | Accepted | Existing v1.2 statutory discount/operator console direction and Annex E-2/E-3. | POS/Invoicing must consume Senior/PWD outcomes for Sales Invoice, BIR Sales Summary, sales books, EJ, POSLog, and audit. |
| POS-D018 | NAAC and Solo Parent are future-supported statutory entitlement categories. | Accepted | Annex E-4/E-5 require report structures; current implementation is not active. | Model must accommodate NAAC and Solo Parent later. They are not permanently unsupported and should not be blocked out of fiscal structures. |
| POS-D019 | Diplomat VAT Privilege / VAT Exemption is an active entitlement / fiscal treatment category. | Accepted | User decision; account for BIR Revenue Memorandum Order No. 10-2019. | Model as VAT privilege / VAT exemption, not an ordinary commercial discount. Exact evidence, wording, reporting, and retention remain open for compliance/accounting confirmation. |
| POS-D020 | Fiscal state must be tamper-evident, append-only, and continuity-proven across restore/failover. | Accepted | User decision and BIR anti-tamper/counter integrity expectations. | POS Server must never resume from lower counters, lower Grand Total Amount, lower Z-counter, or earlier Sales Invoice sequence than the last externally anchored fiscal state. |

## Still Open Decisions

| ID | Open decision | Why unresolved | Owner candidate |
| --- | --- | --- | --- |
| POS-P002 | Exact Sales Invoice, adjustment document, and other fiscal document numbering format and scope. | Sources mention six running digits, reset counter, and 1+15 / 2+15 patterns. Sales Invoice is decided, but exact sequence format still needs confirmation. | Finance/legal/accounting with POS accreditation advisor. |
| POS-P004 | How MIN, PTU, serial number, terminal number, software version, and supplier accreditation metadata are assigned across Site POS Server and terminals/channels. | Site-level POS Server is accepted; field assignment across server, APM, cashier terminal, EC/continuity terminal, WebPay, and operator-assisted channel remains unresolved. | Architecture plus compliance/accounting. |
| POS-P005 | Offline fiscal issuance policy. | Annex F gap requires online/offline indicator; BIR sources require no gaps, immutable counters, and report reconciliation. | Architecture, operations, compliance. |
| POS-P006 | Void/refund/cancel/return workflow sequencing between POS Server and Central PMS/payment providers. | POS Server owns fiscal adjustment documents; Central PMS owns payment/refund finality and provider outcome truth. | Architecture plus payments/compliance. |
| POS-P007 | Exact VAT/tax treatment per taxpayer, Site, transaction type, entitlement type, and line item. | POS Server must support fiscal line classifications, but exact treatment must be configured and confirmed by finance/accounting or BIR advisor. | Finance/accounting/tax with architecture. |
| POS-P008 | Exact Diplomat VAT Privilege / VAT Exemption validation, evidence, fiscal wording, reporting, and retention. | Category is active and must be modeled as VAT privilege/exemption; implementation details under RMO No. 10-2019 need compliance confirmation. | Finance/accounting/compliance. |
| POS-P009 | Fiscal retention storage target and retention duration implementation. | BIR blueprint says 10 years for BIR-relevant files; v1.2 operational TTLs are not the same as fiscal retention. | Architecture/security/compliance. |
| POS-P010 | Implementation design for tamper-evident fiscal state, DR/restore continuity, and external anchoring. | Continuity requirement is accepted, but exact mechanism is a POS Server System Design item. | Architecture/security/compliance. |

## Closed Pending Decisions

| ID | Closed item | Resolution |
| --- | --- | --- |
| POS-P001 | Whether parking fee fiscal document must be Sales Invoice, Official Receipt, or conditionally one of both. | Decided: parking payment fiscal output for ExitPass v1.3 is Sales Invoice. |
| POS-P003 | Whether Site-level POS Server can satisfy the target architecture. | Decided: BIR recommended the Site-level POS Server model. Remaining open issue is fiscal identity field assignment, not the server model itself. |

## Non-Decisions

| ID | Non-decision | Note |
| --- | --- | --- |
| POS-N001 | No database schema is proposed in this artifact set. | This is documentation planning only. |
| POS-N002 | No source code change is proposed. | Code and schema must remain untouched. |
| POS-N003 | No final BRD wording is proposed. | These artifacts are intended to feed a later BRD. |
| POS-N004 | No DOCX output is generated. | Markdown-only output per task instruction. |
