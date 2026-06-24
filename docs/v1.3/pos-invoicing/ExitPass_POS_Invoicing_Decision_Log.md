# ExitPass POS/Invoicing Decision Log

Version: v1.3 initial planning artifact
Status: Draft for planning only
Generated: 2026-06-24

## Decisions

| ID | Decision | Status | Source / rationale | Impact |
| --- | --- | --- | --- | --- |
| POS-D001 | Treat POS/Invoicing as a platform-wide ExitPass capability, not only an AutoPay Machine feature. | Accepted for planning | User architecture direction; Hikvision sources are APM-focused but BIR fiscal outputs apply to transactions across channels. | BRD must model WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future channels under POS/Invoicing. |
| POS-D002 | Use one Site-level POS Server per site or parking operation boundary. | Accepted for planning | User architecture direction; v1.2 already has site/site-group boundaries. | Site resolution becomes the fiscal routing key. Fiscal numbering and reports are scoped to the resolved POS Server unless BIR confirms a different scope. |
| POS-D003 | Model payment channels and terminals as children of the Site-level POS Server. | Accepted for planning | User architecture direction; BIR sources require terminal identity and machine/POS identifiers. | Terminal/channel metadata must exist for WebPay, APM, Cashier POS, EC Device, operator-assisted, and future channels. |
| POS-D004 | Central PMS remains authority for payment finality. | Accepted for planning | User architecture direction; v1.2 DDL/services define payment attempts, confirmations, provider outcomes, and finality paths. | POS Server consumes confirmed payment context; it must not independently mark a payment as final. |
| POS-D005 | Central PMS remains authority for ExitAuthorization. | Accepted for planning | User architecture direction; v1.2 DDL/services define `core.exit_authorizations` issue/consume behavior. | POS fiscal issuance must not grant gate exit. ExitAuthorization workflows must remain in Central PMS/Gate Integration. |
| POS-D006 | POS Server owns fiscal issuance and fiscal reports. | Accepted for planning | BIR blueprint, Annex E, Annex G, Hikvision gap/checklist. | POS Server owns invoice/receipt numbering, reset counter, X-read, Z-read, BIR sales summary, void/refund fiscal controls, cashier/session accountability, audit trail, EJ, POSLog, and fiscal reporting. |
| POS-D007 | Keep fiscal ledger/report storage separate from normal operational analytics. | Accepted for planning | BIR/EJ/retention/hash requirements exceed ordinary operational reporting. | v1.3 design should include immutable fiscal document snapshots and EJ/POSLog/report exports with long retention. |
| POS-D008 | Printed fiscal outputs should be simplified, while complete detail remains in digital records. | Accepted for planning | Hikvision gap analysis says examiner found printed receipt/X/Z too long and recommends Annex D-style printouts plus detailed JSON/EJ/POSLog/PDF/backend exports. | BRD should separate printed layout requirements from canonical fiscal payload requirements. |
| POS-D009 | BIR Sales Summary is a required first-class report. | Accepted for planning | Annex E-1; Hikvision gap says Annex F Item 45(d)(1) requires BIR Sales Summary. | Do not treat sales summary as optional analytics. It must reconcile to SI/OR sequence, Z-counter, reset counter, VAT and deductions. |
| POS-D010 | Reprint operations are controlled fiscal actions. | Accepted for planning | Hikvision gap analysis requires Sales Invoice, X-Read, Z-Read, and EJ reprints with label and logging. | Reprints must not alter original fiscal documents. Every reprint requires audit logging and clear print labeling. |

## Pending Decisions

| ID | Pending decision | Why unresolved | Owner candidate |
| --- | --- | --- | --- |
| POS-P001 | Whether parking fee fiscal document must be Sales Invoice, Official Receipt, or conditionally one of both. | Hikvision gap says switch OR to Sales Invoice; Annex G allows title based on sale of goods/services and applicable document type. | Finance/legal/accounting with BIR accreditation advisor. |
| POS-P002 | Exact document numbering format and scope. | Sources mention at least six running digits, reset counter, and 1+15 / 2+15 digit patterns. Need authoritative rule for SI/OR, void/return/refund/cancel numbers. | Finance/legal/accounting with POS accreditation advisor. |
| POS-P003 | Whether Site-level POS Server can satisfy BIR "machine" requirements, or each terminal/APM needs separate accredited machine identity. | BIR/Hikvision source language focuses on sales machine, serial number, MIN, PTU, non-volatile memory; platform direction proposes server-level fiscal authority. | Architecture plus BIR accreditation advisor. |
| POS-P004 | How MIN, PTU, serial number, terminal number, software version, and supplier accreditation metadata are assigned across server and terminal/channel. | Annex G and BIR blueprint require these fields, but platform server/channel mapping is not yet defined. | Architecture plus compliance. |
| POS-P005 | Offline fiscal issuance policy. | Annex F gap requires online/offline indicator; BIR sources require no gaps, immutable counters, and report reconciliation. | Architecture, operations, compliance. |
| POS-P006 | Void/refund/cancel/return workflow sequencing between POS Server and Central PMS/payment providers. | POS Server owns fiscal adjustment documents; Central PMS owns payment finality and provider outcome truth. | Architecture plus payments/compliance. |
| POS-P007 | Tax computation source of truth for VATable, VAT, VAT-exempt, zero-rated, statutory discounts, coupons, and service charges. | v1.2 tariff snapshots store gross/payable and discount amounts but do not fully cover all fiscal tax report fields. | Architecture plus finance/tax. |
| POS-P008 | Whether NAAC and Solo Parent discount/report support is in v1.3 scope. | Annex E-4/E-5 require reports; v1.2 statutory implementation appears centered on Senior/PWD. | Product/compliance. |
| POS-P009 | Fiscal retention storage target and retention duration implementation. | BIR blueprint says 10 years for BIR-relevant files; v1.2 operational TTLs are not the same as fiscal retention. | Architecture/security/compliance. |
| POS-P010 | DR/restore treatment for reset counter, Z-counter, grand total, sequence state, and EJ hash continuity. | BIR sources require non-resettable/tamper-evident behavior; platform implementation may be database/server-based. | Architecture/security/compliance. |

## Non-Decisions

| ID | Non-decision | Note |
| --- | --- | --- |
| POS-N001 | No database schema is proposed in this artifact set. | The task only asked for initial planning artifacts. |
| POS-N002 | No source code change is proposed. | Code and schema were intentionally not modified. |
| POS-N003 | No final BRD wording is proposed. | These artifacts are intended to feed a later BRD. |
| POS-N004 | No DOCX output is generated. | Markdown-only output per task instruction. |
