# ExitPass POS/Invoicing Decision Recommendations

Version: v1.3 decision update planning artifact
Status: Draft for planning only
Generated: 2026-06-25

## Classification Legend

| Classification | Meaning |
| --- | --- |
| Decided | Review decision is sufficient for BRD planning. |
| Still open but non-blocking for BRD | BRD can state capability and leave implementation/configuration detail open. |
| Blocking for POS Server System Design | BRD can proceed, but POS Server architecture/design cannot be finalized. |
| Blocking for implementation | Implementation should not start without resolution. |
| Deferred post-v1.3 | Not active for v1.3 delivery, but model should avoid blocking future support. |

## Question Recommendation Matrix

| Question ID | Current classification | Recommended default | Rationale | Risk if decided incorrectly | Blocks POS/Invoicing BRD | Blocks POS Server System Design | Blocks implementation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| POS-Q001 | Decided | Use Sales Invoice as primary parking payment fiscal output. | BIR guidance and review decision. | Wrong document type could invalidate accreditation samples and fiscal outputs. | No | No | No |
| POS-Q002 | Decided | Use Site-level POS Server model. | BIR recommended model; user decision. | Channel-specific independent POS systems would fragment counters and reports. | No | No | No |
| POS-Q003 | Blocking for implementation | Use configurable Sales Invoice and adjustment sequences until BIR/accounting confirms exact format. | Sales Invoice is decided, but numbering pattern still requires confirmation. | Incorrect sequence format can break BIR compliance. | No | Yes | Yes |
| POS-Q004 | Blocking for implementation | Store reset counter separately and make print/append behavior configurable pending BIR signoff. | Reset behavior is decided; display/number append is not. | Incorrect printed number could fail sample review. | No | Yes | Yes |
| POS-Q005 | Decided | Use resolved ExitPass Site or parking operation boundary as POS Server boundary. | Review decision. | Wrong boundary could mix fiscal counters across Sites. | No | No | No |
| POS-Q006 | Decided | Register all payment channels as terminals/channels under Site POS Server. | Review decision. | Independent channel POS design would duplicate fiscal authorities. | No | No | No |
| POS-Q007 | Blocking for POS Server System Design | Treat WebPay as a non-physical fiscal channel with assigned terminal identity pending BIR confirmation. | WebPay has no hardware serial/printer. | Missing identity could block WebPay Sales Invoice issuance. | No | Yes | Yes |
| POS-Q008 | Blocking for POS Server System Design | POS Server issues the Sales Invoice payload; APM prints/renders POS Server-issued document pending BIR confirmation. | Preserves Site POS Server authority while supporting APM printer. | APM-local issuance could split fiscal authority; server-only printing may fail APM accreditation expectations. | No | Yes | Yes |
| POS-Q009 | Blocking for POS Server System Design | Use Site-level Z-close with terminal/cashier X-read accountability views unless BIR requires separate terminal Z-close. | Aligns with Site POS Server model while preserving cashier accountability. | Wrong aggregation could make X/Z reports unreconcilable. | No | Yes | Yes |
| POS-Q010 | Decided | Fiscal issuance before ExitAuthorization. | Review decision. | Exit could be authorized without fiscal document. | No | No | No |
| POS-Q011 | Decided | Enter fiscal issuance exception/retry; do not reverse payment automatically; withhold ExitAuthorization. | Review decision. | Reversing payment incorrectly or releasing exit too early creates control gaps. | No | No | No |
| POS-Q012 | Decided | Central PMS records fiscal reference before issuing ExitAuthorization; recovery resumes from fiscal reference. | Review decision. | Duplicate fiscal issuance or missing authorization recovery. | No | No | No |
| POS-Q013 | Blocking for implementation | Default to no offline Sales Invoice issuance unless explicitly approved; show offline status and queue non-fiscal support actions only. | Offline policy is unresolved and high-risk for counters. | Duplicate or skipped invoice numbers. | No | Yes | Yes |
| POS-Q014 | Blocking for implementation | Require coordinated workflow: payment reversal/refund finality in Central PMS/provider and fiscal adjustment document in POS Server with reconciliation links. | Preserves authority split. | Money movement and fiscal adjustment can diverge. | No | Yes | Yes |
| POS-Q015 | Blocking for implementation | BRD states fiscal line support; exact treatment is finance/accounting configuration. | Tax treatment varies by taxpayer/Site/line/entitlement. | Incorrect VAT/non-VAT/exempt treatment. | No | Yes | Yes |
| POS-Q016 | Still open but non-blocking for BRD | Treat Senior/PWD as immediate workflows and require fiscal/report mapping. | Operational support exists or is planned in v1.2 flow. | Wrong VAT/discount display or report treatment. | No | Yes | Yes |
| POS-Q017 | Still open but non-blocking for BRD | Include NAAC and Solo Parent in model and report structure as future-supported categories. | Annex E-4/E-5 require structures; workflows are not active. | Model may need redesign later if omitted. | No | No | No |
| POS-Q018 | Blocking for implementation | Create explicit fiscal line catalog/classification for all fee, discount, privilege, coupon, penalty, lost ticket, overstay, service charge, and adjustment lines. | BIR reports require itemized fiscal treatment. | Incomplete reports and incorrect invoice totals. | No | Yes | Yes |
| POS-Q019 | Blocking for implementation | Use evidence/reference model and avoid duplicating personal data until legal/privacy confirms required storage. | Annex E and entitlement evidence can conflict with privacy minimization. | Overcollection or inability to produce reports. | No | Yes | Yes |
| POS-Q020 | Blocking for implementation | Separate fiscal retention from evidence retention; fiscal records follow BIR retention, evidence follows confirmed privacy/compliance policy. | Different data classes may require different retention. | Retaining too little breaks audit; retaining too much creates privacy risk. | No | Yes | Yes |
| POS-Q021 | Blocking for POS Server System Design | Use tamper-evident append-only fiscal state with external anchoring for counters, Grand Total Amount, sequence, EJ hash, and last event timestamp. | Review decision requires continuity proof. | Rollback, duplication, skipped records, or failed audit. | No | Yes | Yes |
| POS-Q022 | Blocking for POS Server System Design | Require supervised recovery audit before fiscal issuance resumes if continuity cannot be proven. | Review decision. | Restored system may issue from stale counters. | No | Yes | Yes |
| POS-Q023 | Blocking for implementation | Use idempotent issuance and explicit failed/reserved/voided fiscal states; final behavior needs BIR signoff. | Sequence gaps are high-risk. | Duplicate or missing Sales Invoice numbers. | No | Yes | Yes |
| POS-Q024 | Blocking for implementation | Use trusted time source, block manual rollback, log drift, and include terminal/server clock health. | BIR/Hikvision sources require clock integrity. | Incorrect fiscal timestamps and audit disputes. | No | Yes | Yes |
| POS-Q025 | Blocking for implementation | Define fiscal RBAC for reprint, adjustment, Z-close, export, reset, restore, recovery, and configuration changes. | Fiscal actions are privileged and audit-sensitive. | Unauthorized fiscal mutation or weak accountability. | No | Yes | Yes |
| POS-Q026 | Still open but non-blocking for BRD | BRD requires reprint label/logging; exact label placement requires BIR sample review. | Hikvision gap confirms requirement. | Sample print rejection. | No | No | Yes |
| POS-Q027 | Blocking for implementation | Support TXT EJ replica, PDF/JSON exports, and ARTS POSLog unless BIR/accounting narrows scope. | Sources cite multiple formats. | Missing mandated export format. | No | Yes | Yes |
| POS-Q028 | Blocking for POS Server System Design | Use one canonical fiscal event model that renders EJ text and POSLog JSON/ARTS from same source event. | Prevents divergence. | EJ/POSLog mismatch. | No | Yes | Yes |
| POS-Q029 | Blocking for implementation | Identify supplier/applicant before final sample package and footer generation. | Footer/accreditation fields depend on roles. | Wrong legal entity in fiscal documents. | No | Yes | Yes |
| POS-Q030 | Blocking for POS Server System Design | Assign Hikvision responsibility for APM hardware/print integration; ExitPass POS Server owns fiscal issuance unless BIR says otherwise. | Aligns accepted Site POS Server model with APM realities. | Vendor responsibility gaps. | No | Yes | Yes |
| POS-Q031 | Still open but non-blocking for BRD | BRD should list expected sample categories and mark final accreditation sample set for advisor confirmation. | Sample list is accreditation detail. | Missing sample delays accreditation. | No | No | Yes |
| POS-Q032 | Still open but non-blocking for BRD | Require BIR/advisor signoff on Annex D-1/D-2 print layouts. | Prior local extraction could not fully verify PDF layout. | Print layout rejection. | No | No | Yes |
| POS-Q033 | Blocking for implementation | Treat Diplomat VAT Privilege / VAT Exemption evidence as compliance-confirmed required document set under RMO No. 10-2019. | Active category cannot be implemented without evidence rule. | Invalid exemption grant or overcollection of evidence. | No | Yes | Yes |
| POS-Q034 | Blocking for implementation | Model privilege at fiscal line level so whole-transaction or line-specific application can be configured. | Exact scope is open. | Wrong VAT exemption scope. | No | Yes | Yes |
| POS-Q035 | Blocking for implementation | Require compliance/accounting confirmation for wording, summary treatment, EJ/POSLog, reporting, and retention before implementation. | Diplomat VAT Privilege / VAT Exemption is active but details are unresolved. | Incorrect Sales Invoice and statutory reporting. | No | Yes | Yes |

## Proposed Questions for BIR / Accounting Advisor

Only still-open items are included below.

1. How should MIN, PTU, serial number, terminal number, software version, and supplier accreditation metadata be assigned between Site POS Server, APM terminal, Cashier POS terminal, EC Device / Continuity Terminal, WebPay channel, operator-assisted channel, and future channels?
2. What fiscal terminal identity should WebPay use when there is no physical printer or hardware serial?
3. Can an APM print a POS Server-issued Sales Invoice payload, or must the APM itself be treated as the issuing fiscal machine for printing purposes?
4. What exact Sales Invoice and adjustment document numbering patterns should ExitPass use, including reset counter placement or printing?
5. What exact VAT/tax treatment applies to parking fees, lost ticket fees, penalties, overstay charges, service charges, coupons, statutory discounts, and other fiscal adjustments?
6. What exact treatment applies to Diplomat VAT Privilege / VAT Exemption under BIR RMO No. 10-2019?
7. What supporting evidence is required for Diplomat VAT Privilege / VAT Exemption: VAT Certificate, VAT Identification Card, or other BIR/DFA-issued evidence?
8. What buyer/customer identity fields are required for Diplomat VAT Privilege / VAT Exemption Sales Invoices?
9. Does Diplomat VAT Privilege / VAT Exemption apply to the whole parking transaction or only specific fiscal lines?
10. What Sales Invoice wording, BIR Sales Summary treatment, EJ/POSLog treatment, reporting, and evidence retention rules apply to Diplomat VAT Privilege / VAT Exemption?
11. Should NAAC and Solo Parent report structures be included in v1.3 fiscal report models even if operational workflows are future-supported?
12. Is offline fiscal issuance allowed for APM, cashier, EC/continuity, or other terminals? If yes, what sequence, counter, and reconciliation controls are required?
13. What DR/restore and counter continuity controls are acceptable for a Site-level POS Server, including externally anchored last Grand Total Amount, reset counter, Z-counter, latest Sales Invoice number, latest EJ hash, and last fiscal event timestamp?
14. What export formats are mandatory for EJ, Sales Invoice, X-Read, Z-Read, BIR Sales Summary, Annex E reports, and POSLog?
15. What final accreditation sample set should ExitPass prepare, including regular parking, mixed tender, Senior/PWD, Diplomat VAT Exemption, void/refund/cancel, X/Z, EJ, POSLog, BIR Sales Summary, and Annex E reports?
