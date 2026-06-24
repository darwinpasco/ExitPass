# ExitPass POS/Invoicing Open Questions

Version: v1.3 decision update planning artifact
Status: Draft for planning only
Generated: 2026-06-25

## Reclassified Decisions

| ID | Question | Status | Decision note |
| --- | --- | --- | --- |
| POS-Q001 | For parking fees, should the principal fiscal document be Sales Invoice, Official Receipt, or configurable by taxpayer/transaction type? | Decided | Parking payment fiscal output for ExitPass v1.3 shall be Sales Invoice. Other fiscal document types remain design considerations for adjustments or other transaction types. |
| POS-Q002 | Does the BIR accreditation path accept a Site-level POS Server issuing for all channels, or must each APM/cashier terminal be separately treated as a sales machine with separate MIN/PTU/serial? | Decided | BIR recommended the Site-level POS Server model. Remaining open issue is MIN/PTU/serial/software/accreditation assignment across server and terminals/channels. |
| POS-Q005 | What exactly defines a site or parking operation boundary for one POS Server? | Decided for BRD | Use the resolved ExitPass Site or parking operation boundary as the POS Server fiscal boundary. Exact mapping to taxpayer branch/BIR-registered location remains part of fiscal identity configuration. |
| POS-Q006 | Can one POS Server cover multiple lanes and terminals, including WebPay, APM, Cashier POS, EC Device, and operator-assisted payment? | Decided | Yes. WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future channels are terminals/channels under the Site-level POS Server. |
| POS-Q010 | Should fiscal issuance occur before or after Central PMS issues ExitAuthorization? | Decided | Fiscal issuance must succeed before Central PMS issues ExitAuthorization. |
| POS-Q011 | What happens if Central PMS records payment finality but POS Server fiscal issuance fails or times out? | Decided | Payment finality is not reversed automatically. ExitAuthorization is withheld. Case enters fiscal issuance exception/retry workflow with customer/operator messaging. |
| POS-Q012 | What happens if POS Server issues a Sales Invoice but Central PMS fails before ExitAuthorization is issued? | Decided at sequence level | Central PMS records fiscal issuance reference before issuing ExitAuthorization. Recovery must resume from recorded fiscal reference; POS Server never issues authorization. |

## Remaining Open Questions

| ID | Question | Current status | Source / reason | Blocking impact |
| --- | --- | --- | --- | --- |
| POS-Q003 | What is the exact BIR numbering pattern for Sales Invoice, void, return, refund, cancelled documents, credit memo/debit memo equivalents, and any other adjustment documents for this taxpayer and system type? | Open | Sales Invoice is decided, but sources mention six running digits, reset counter, and 1+15 / 2+15 patterns. | Blocks implementation of sequence generator and final sample outputs. |
| POS-Q004 | Does reset counter append to the Sales Invoice number, print separately, or both? | Open | Annex G references running serial number appended with reset counter if applicable; Annex E-1 has a Reset Counter column. | Blocks final numbering layout and report rendering. |
| POS-Q007 | What fields must identify a WebPay channel as a fiscal terminal when there is no physical printer or hardware serial? | Open | BIR/Annex G require POS terminal, serial, MIN, software version, and related fiscal identity fields. | Blocks WebPay fiscal terminal identity implementation. |
| POS-Q008 | Must the APM print directly from its local printer, or can the Site POS Server generate the Sales Invoice and send a print job/payload to the APM terminal? | Open | BIR/Hikvision APM documents assume APM thermal printing, while Site POS Server is accepted as fiscal authority. | Blocks APM print integration and outage behavior. |
| POS-Q009 | Is there one Z-close per Site POS Server, per terminal, per cashier/session, or both terminal-level and server-level Z reports? | Open | Annex D/G/Hikvision discuss X/Z; Site POS Server is site-level but cashier accountability may require terminal/session views. | Blocks X/Z aggregation and cashier accountability design. |
| POS-Q013 | Can POS Server ever issue a Sales Invoice based on pending offline payment evidence, or only after Central PMS confirmed finality? | Open | Offline indicator is required, but no offline fiscal issuance policy is defined. | Blocks offline APM/cashier/continuity operation. |
| POS-Q014 | Which system initiates refunds and voids: Central PMS/payment provider first, or POS fiscal adjustment document first? | Open | POS Server owns fiscal adjustment documents; Central PMS owns payment/refund finality. | Blocks void/refund/cancel/return workflow design. |
| POS-Q015 | What is the authoritative tax treatment of parking fees by Site, taxpayer, transaction type, entitlement type, and line item? | Open | Annex E and Annex G require VAT/non-VAT/VAT-exempt/zero-rated breakdowns. | Blocks final tax configuration and fiscal calculations. |
| POS-Q016 | How should Senior Citizen and PWD entitlement outcomes be represented on Sales Invoice, BIR Sales Summary, Annex E reports, EJ, POSLog, and audit? | Open details | Senior/PWD workflows are immediate, but fiscal wording and tax/report treatment still need confirmation. | Blocks implementation detail, not BRD-level capability statement. |
| POS-Q017 | Should NAAC and Solo Parent report structures be included in v1.3 even though operational workflows are future-supported? | Open | Annex E-4/E-5 require report structures; NAAC/Solo Parent are future-supported categories. | May block report model design; does not block primary BRD if documented as future-supported. |
| POS-Q018 | How should coupons, merchant-sponsored discounts, free parking, lost ticket fees, penalties, service charges, overstay charges, and other fiscal adjustments be itemized and classified? | Open | BIR blueprint mentions line items, discounts, lost ticket fees; v1.2 has coupon and tariff concepts. | Blocks fiscal line item catalog implementation. |
| POS-Q019 | Should entitlement personal details required by Annex E sales books be stored in POS Server, referenced from Operator Console evidence, or derived from a compliance vault? | Open | Annex E reports include identity details; v1.2 privacy design minimizes stored personal data. | Blocks privacy and report generation design. |
| POS-Q020 | What is the retention period for entitlement personal data versus fiscal reports, EJ, Sales Invoice snapshots, and POSLog? | Open | BIR blueprint says 10 years for BIR-relevant files; v1.2 evidence retention placeholders are shorter. | Blocks retention implementation. |
| POS-Q021 | How should tamper-evident fiscal state be implemented for Grand Total Amount, reset counter, Z-counter, Sales Invoice sequence, EJ hash, and last fiscal event timestamp? | Open system design | BIR blueprint assumes non-volatile/tamper-resistant machine memory; Site POS Server implementation must prove continuity. | Blocks POS Server System Design and implementation. |
| POS-Q022 | What is the approved recovery procedure after POS Server database restore, failover, repair, backup restore, or fiscal counter continuity failure? | Open system design | System must never resume from lower counters or earlier Sales Invoice sequence than last externally anchored fiscal state. | Blocks POS Server System Design and implementation. |
| POS-Q023 | How are Sales Invoice sequence gaps handled for failed, timed-out, or abandoned issuance attempts? | Open | BIR sources require sequential/no-gap behavior and adjustment logging. | Blocks idempotency and document reservation strategy. |
| POS-Q024 | What clock authority is required for POS Server and terminals? | Open | Hikvision checklist says reject date/time changes except via NTP; BIR blueprint requires date rollback prevention. | Blocks timestamping and audit integrity implementation. |
| POS-Q025 | Which administrative roles can perform reprint, void/refund/cancel, Z-close, export, restore, reset, recovery, and configuration changes? | Open | Annex G and Hikvision sources require audit trail and controlled adjustments; v1.2 has roles but no fiscal roles. | Blocks RBAC implementation. |
| POS-Q026 | Do reprinted Sales Invoice, X-Read, Z-Read, and EJ outputs need exact text labels and placement approved by BIR? | Open | Hikvision gap says reprints must be labeled and logged. | Blocks final print layout approval. |
| POS-Q027 | What export formats are mandatory versus optional: TXT, PDF, JSON, XML, ARTS POSLog? | Open | Annex G says e-journal in `.txt`; BIR blueprint/Hikvision checklist mention PDF+JSON and ARTS POSLog. | Blocks export contract implementation. |
| POS-Q028 | How must POSLog reconcile with EJ when POSLog is JSON/ARTS but e-journal is a text replica of printed fiscal documents? | Open | Sources require both but with different formats. | Blocks canonical fiscal event model. |
| POS-Q029 | Who is the software supplier/applicant for accreditation: ExitPass, PPMC, Hikvision, or another entity? | Open | BIR blueprint separates software provider, POS user/PTU applicant, and hardware supplier. | Blocks footer, supplier accreditation, manuals, source documentation, and submission package. |
| POS-Q030 | Is Hikvision still responsible for APM fiscal printing/hardware controls if ExitPass Site POS Server owns fiscal issuance? | Open | Hikvision documents are APM-focused; Site POS Server is accepted. | Blocks vendor responsibility matrix. |
| POS-Q031 | What final sample set is required for accreditation: regular, discount, card, mixed tender, void/refund/cancel, X/Z, EJ, POSLog, BIR Sales Summary, Annex E sales books, Diplomat VAT exemption samples? | Open | Annex G and Hikvision gap/checklist list sample outputs; Diplomat VAT Privilege/VAT Exemption is active. | Blocks accreditation evidence package. |
| POS-Q032 | Who signs off exact Annex D-1/D-2 layouts after local PDF text extraction was inconclusive? | Open | Annex D PDF samples were not fully text-extractable in the prior local session. | Blocks final print layout fidelity. |
| POS-Q033 | What exact supporting document is required for Diplomat VAT Privilege / VAT Exemption: VAT Certificate, VAT Identification Card, or other BIR/DFA-issued evidence? | Open | Diplomat VAT Privilege / VAT Exemption is active and must account for BIR RMO No. 10-2019. | Blocks validation workflow and evidence capture. |
| POS-Q034 | Does Diplomat VAT Privilege / VAT Exemption apply to the whole parking transaction or only specific fiscal lines? | Open | Must be modeled as VAT privilege/exemption, not ordinary discount. | Blocks fiscal line classification and Sales Invoice wording. |
| POS-Q035 | What are the Sales Invoice wording, BIR Sales Summary treatment, EJ/POSLog treatment, reporting requirements, and evidence retention rules for Diplomat VAT Privilege / VAT Exemption? | Open | Active entitlement / fiscal treatment category requires compliance/accounting confirmation. | Blocks Diplomat implementation and accreditation samples. |

## Reset Counter Clarification

| Topic | Rule |
| --- | --- |
| Reset counter initial value | Starts from zero. |
| Reset counter increment | Increments by one only for each fiscal reset event. |
| Required reset audit snapshot | Previous Grand Total Amount, previous reset counter, reset timestamp, reset reason, approving user, and recovery/reference notes. |
| Z-counter distinction | Z-counter is separate and advances per Z-reading / fiscal day close. Daily Z-close does not increment reset counter. |

## Fiscal Issuance Exception Rule

If Central PMS records verified payment finality but Sales Invoice issuance fails or times out:

- Payment finality is not reversed automatically.
- ExitAuthorization is not issued yet.
- The case enters controlled fiscal issuance exception/retry workflow.
- Customer/operator messaging must show that payment was received but fiscal issuance is pending and exit authorization is not yet available.
- Manual release, if allowed, must be supervisor-approved, incident-tagged, and reconciliation-tagged.
- POS Server must not issue ExitAuthorization.
- Payment Orchestrator and WebPay must not bypass Central PMS.
