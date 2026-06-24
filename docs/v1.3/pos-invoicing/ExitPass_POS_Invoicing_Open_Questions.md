# ExitPass POS/Invoicing Open Questions

Version: v1.3 initial planning artifact
Status: Draft for planning only
Generated: 2026-06-24

## Fiscal Classification

| ID | Question | Source / reason | Blocking impact |
| --- | --- | --- | --- |
| POS-Q001 | For parking fees, should the principal fiscal document be "Sales Invoice", "Official Receipt", or configurable by taxpayer/transaction type? | Hikvision examiner gap says OR must become Sales Invoice; Annex G supports SI/OR title depending on applicable transaction. | Blocks final receipt/invoice terminology, numbering labels, layouts, report names, and BRD language. |
| POS-Q002 | Does the BIR accreditation path accept a Site-level POS Server issuing for all channels, or must each APM/cashier terminal be separately treated as a sales machine with separate MIN/PTU/serial? | User architecture sets Site-level POS Server; BIR/Hikvision sources are machine/APM-centric. | Blocks machine identity model, counters, PTU mapping, and accreditation package. |
| POS-Q003 | What is the exact BIR numbering pattern for Sales Invoice, Official Receipt, void, return, refund, and cancelled documents for this taxpayer and system type? | Sources mention six running digits, reset counter, 1+15 OR pattern, and 2+15 void OR pattern. | Blocks sequence generator design and sample outputs. |
| POS-Q004 | Does reset counter append to the fiscal document number, print separately, or both? | Annex G references running serial number appended with reset counter if applicable; Annex E-1 has a Reset Counter column. | Blocks numbering layout and report fields. |

## Server And Terminal Model

| ID | Question | Source / reason | Blocking impact |
| --- | --- | --- | --- |
| POS-Q005 | What exactly defines a "site or parking operation boundary" for one POS Server: physical site, site group, taxpayer branch, BIR-registered location, parking facility, or merchant scope? | User architecture says each Site or parking operation boundary should have one POS Server; v1.2 has site and site group. | Blocks routing, fiscal scope, reporting scope, and failover. |
| POS-Q006 | Can one POS Server cover multiple lanes and terminals, including WebPay, APM, Cashier POS, EC Device, and operator-assisted payment? | Platform direction says yes, but BIR machine/PTU mapping needs confirmation. | Blocks channel/terminal registration model. |
| POS-Q007 | What fields must identify a WebPay channel as a fiscal terminal when there is no physical printer or hardware serial? | BIR/Annex G require POS terminal, serial, MIN, software version. | Blocks WebPay invoice issuance and print/download metadata. |
| POS-Q008 | Must the APM print directly from its local printer, or can the Site POS Server generate the fiscal document and send a print job to the APM terminal? | BIR/Hikvision APM documents assume APM thermal printing. | Blocks APM integration pattern and outage behavior. |
| POS-Q009 | Is there one Z-close per Site POS Server, per terminal, per cashier/session, or both terminal-level and server-level Z reports? | Annex D/G/Hikvision discuss X/Z; user architecture makes POS Server site-level. | Blocks X/Z aggregation and cashier accountability. |

## Payment And Authorization Boundaries

| ID | Question | Source / reason | Blocking impact |
| --- | --- | --- | --- |
| POS-Q010 | Should fiscal issuance occur before or after Central PMS issues ExitAuthorization? | Central PMS owns ExitAuthorization; POS Server owns fiscal issuance. | Blocks transaction choreography and recovery behavior when issuance or authorization fails. |
| POS-Q011 | What happens if Central PMS records payment finality but POS Server fiscal issuance fails or times out? | Payment finality and fiscal issuance are separate authorities. | Blocks retry, compensation, customer messaging, gate release policy, and reconciliation. |
| POS-Q012 | What happens if POS Server issues an invoice/receipt but Central PMS fails before ExitAuthorization is issued? | POS issuance cannot imply gate authorization. | Blocks exception workflow and customer support process. |
| POS-Q013 | Can POS Server ever issue an invoice/receipt based on pending offline payment evidence, or only after Central PMS confirmed finality? | Offline indicator is required, but no offline fiscal policy is defined. | Blocks offline APM/cashier operation. |
| POS-Q014 | Which system initiates refunds and voids: Central PMS/payment provider first, or POS fiscal adjustment document first? | POS owns fiscal adjustment documents; Central PMS owns payment/refund finality. | Blocks void/refund/cancel/return workflow design. |

## Tax, Discounts, And Reports

| ID | Question | Source / reason | Blocking impact |
| --- | --- | --- | --- |
| POS-Q015 | What is the authoritative tax treatment of parking fees: VATable, non-VAT, VAT-exempt, zero-rated, percentage tax, or site/taxpayer configurable? | Annex E and Annex G require VAT/non-VAT breakdowns. | Blocks fiscal line computation and report totals. |
| POS-Q016 | Are Senior Citizen and PWD parking discounts always applicable, and how should VAT removal be represented on fiscal documents? | Annex E-2/E-3; v1.2 operator console statutory discount workflows. | Blocks discount lines and sales book reporting. |
| POS-Q017 | Are National Athletes and Coaches and Solo Parent discount reports required in v1.3 even if operational workflows do not yet support them? | Annex E-4/E-5 require reports. | Blocks report scope and discount entitlement model. |
| POS-Q018 | How should coupons, merchant-sponsored discounts, free parking, lost ticket fees, penalties, service charges, and overstay charges be itemized fiscally? | BIR blueprint mentions line items, discounts, lost ticket fees; v1.2 has coupon and tariff concepts. | Blocks fiscal line item catalog. |
| POS-Q019 | Should statutory discount personal details required by Annex E sales books be stored in POS Server, referenced from Operator Console evidence, or derived from a compliance vault? | Annex E reports include names/IDs/TINs; v1.2 privacy design minimizes stored personal data. | Blocks privacy and report generation design. |
| POS-Q020 | What is the retention period for discount personal data versus fiscal reports and EJ? | BIR blueprint says 10 years for BIR-relevant files; v1.2 evidence retention placeholders are shorter. | Blocks privacy, retention, and storage policy. |

## Fiscal Integrity And Operations

| ID | Question | Source / reason | Blocking impact |
| --- | --- | --- | --- |
| POS-Q021 | How should non-resettable grand total, Z-counter, and reset counter be implemented in a server/cloud/database architecture? | BIR blueprint assumes non-volatile/tamper-resistant machine memory. | Blocks fiscal state storage and DR design. |
| POS-Q022 | What is the approved recovery procedure after POS Server database restore, failover, or counter corruption? | BIR sources require no gaps and counter integrity. | Blocks HA/DR and audit procedure. |
| POS-Q023 | How are invoice sequence gaps handled for failed, timed-out, or abandoned issuance attempts? | BIR sources require sequential/no gaps and void/cancel logging. | Blocks idempotency and document reservation strategy. |
| POS-Q024 | What clock authority is required for POS Server and terminals? | Hikvision checklist says reject date/time changes except via NTP; BIR blueprint requires date rollback prevention. | Blocks timestamping and audit integrity. |
| POS-Q025 | Which administrative roles can perform reprint, void/refund/cancel, Z-close, export, restore, and configuration changes? | Annex G and Hikvision sources require audit trail and controlled adjustments; v1.2 has roles but no fiscal roles. | Blocks RBAC design. |
| POS-Q026 | Do reprinted Sales Invoice, X-Read, Z-Read, and EJ outputs need exact text labels and placement approved by BIR? | Hikvision gap says reprints must be labeled and logged. | Blocks print layout finalization. |
| POS-Q027 | What export formats are mandatory versus optional: TXT, PDF, JSON, XML, ARTS POSLog? | Annex G says e-journal in `.txt`; BIR blueprint/Hikvision checklist mention PDF+JSON and ARTS POSLog. | Blocks export contract. |
| POS-Q028 | How must POSLog reconcile with EJ when POSLog is JSON/ARTS but e-journal is a text replica of printed documents? | Sources require both but with different formats. | Blocks canonical fiscal event model. |

## Accreditation Package

| ID | Question | Source / reason | Blocking impact |
| --- | --- | --- | --- |
| POS-Q029 | Who is the software supplier/applicant for accreditation: ExitPass, PPMC, Hikvision, or another entity? | BIR blueprint separates software provider, POS user/PTU applicant, and hardware supplier. | Blocks footer, supplier accreditation, manuals, source documentation, and submission package. |
| POS-Q030 | Is Hikvision still responsible for APM fiscal functionality if ExitPass Site POS Server owns fiscal issuance? | Hikvision documents are APM-focused; user architecture is platform-wide. | Blocks vendor responsibility matrix. |
| POS-Q031 | What final sample set is required for accreditation: regular, discount, card, mixed tender, void/refund/cancel, X/Z, EJ, POSLog, BIR Sales Summary, Annex E sales books? | Annex G and Hikvision gap/checklist list sample outputs. | Blocks acceptance test evidence list. |
| POS-Q032 | Who signs off exact Annex D-1/D-2 layouts after local PDF text extraction was inconclusive? | Annex D PDF samples were not fully text-extractable in this environment. | Blocks print layout fidelity. |
