# ExitPass POS/Invoicing Source Analysis

Version: v1.3 initial planning artifact
Status: Draft for planning only
Generated: 2026-06-24

## Scope Boundary

This is not the v1.3 BRD. This document identifies source-driven requirements and planning implications for a platform-wide ExitPass POS/Invoicing capability.

ExitPass POS/Invoicing must be modeled as a site-level fiscal capability, not as a Hikvision AutoPay Machine-only requirement. Each site or parking operation boundary should resolve to one Site-level POS Server. WebPay, AutoPay Machine, Cashier POS, EC Device, operator-assisted payment, and future payment channels should be modeled as channels or terminals under that Site-level POS Server.

The resolved site determines which POS Server issues the invoice or receipt.

## Source Set Reviewed

| Source category | Local source |
| --- | --- |
| BIR RMO No. 24-2023 | `D:\Docs\ExitPass\POS\RMO No. 24-2023.pdf`; `D:\Docs\ExitPass\POS\BIR POS Accreditation Requirements.docx` |
| Annex D-1 Sample X-Reading | `D:\Docs\ExitPass\POS\RMO 24-2023 Annex D-1_Sample X-Reading.pdf`; Hikvision gap analysis references to Annex D-1 |
| Annex D-2 Sample Z-Reading | `D:\Docs\ExitPass\POS\RMO 24-2023 Annex D-2_Sample Z-Reading.pdf`; Hikvision gap analysis references to Annex D-2 |
| Annex E-1 to E-5 | `D:\Docs\ExitPass\POS\RMO 24-2023 Annex E-1 to E-5.xlsx` |
| Annex F Functional and Technical Evaluation Checklist | `D:\Docs\ExitPass\POS\RMO 24-2023 ANNEX F_12072022_Functional and Technical Evaluation Checklist_RAF.docx.pdf`; Hikvision gap analysis references to Annex F items |
| Annex G Minutes of Meeting | `D:\Docs\ExitPass\POS\RMO 24-2023 Annex G_Minutes of Meeting_v2_RAF (1).docx` |
| Hikvision APM gap analysis and checklist | `D:\Docs\ExitPass\POS\FINAL GAP ANALYSIS - Hikvision AutoPay Machine BIR Accreditation.docx`; `D:\Docs\ExitPass\POS\Hikvision Developer Checklist for BIR-Compliant Autopay Parking Station.docx` |
| Existing ExitPass v1.2 documents | `D:\Docs\ExitPass\v1.2\*`; repo v1.2 DDL and existing docs under `docs/` |

Note: the RMO main PDF appears image/scanned in this environment, and Annex D/F PDFs were not fully text-extractable using available local tools. Requirements attributed to those sources are therefore based on the file names, the consolidated BIR blueprint, the Annex G text, the Annex E workbook extraction, and the Hikvision examiner gap analysis where those annexes are explicitly referenced. Exact field-by-field PDF validation remains an open question.

## Platform Position

| Position | Planning implication |
| --- | --- |
| POS/Invoicing is platform-wide. | Do not bind fiscal issuance only to Hikvision APM. WebPay, APM, cashier, EC device, operator-assisted, and future channels all need fiscal issuance through the resolved Site-level POS Server. |
| One Site-level POS Server per site or parking operation boundary. | The POS Server is the fiscal authority for numbering, counters, reports, and fiscal audit data for that site. Terminals/channels are children of the server. |
| Central PMS owns payment finality and ExitAuthorization. | POS Server must not independently finalize payment or issue gate authorization. It consumes Central PMS confirmed-payment context and returns fiscal issuance status. |
| POS Server owns fiscal issuance. | Fiscal issuance includes invoice/receipt numbering, reset counter, X-read, Z-read, BIR sales summary, void/refund controls, cashier/session accountability, audit trail, and fiscal reporting. |

## Source-Derived Requirement Map

| Requirement area | Source category | Requirement signal | Planning interpretation |
| --- | --- | --- | --- |
| Fiscal document terminology | Hikvision gap analysis; Annex G; RMO/RR references | Examiner gap says "Official Receipt" must be changed to "Sales Invoice"; Annex G requires title to show "SALES INVOICE", "OFFICIAL RECEIPT", or adjustment title as applicable. | v1.3 must not hard-code "Official Receipt" as the only fiscal output. The channel/product taxonomy must determine whether Sales Invoice, Official Receipt, or adjustment document applies. This is an open legal/accounting question for parking services. |
| Invoice/receipt required header fields | Annex G; BIR blueprint; Hikvision checklist | Business name, address, TIN, VAT/non-VAT TIN, MIN, serial number, software name/version, POS terminal number, transaction date/time, document number. | Site-level POS Server needs tenant/site fiscal profile, machine/server identity, terminal identity, software version, and document sequence metadata. |
| Footer and supplier accreditation fields | Annex G; Annex F via gap analysis; Hikvision gap analysis | Footer must include software supplier name/address/TIN, accreditation number/date/validity, PTU or ATG number/date/validity; supplementary documents must show non-input-tax warning. | POS Server profile must support supplier accreditation and PTU metadata separately from site taxpayer metadata. These fields must be renderable in all fiscal print layouts. |
| Document numbering | BIR blueprint; Hikvision checklist; Annex G | BIR-approved numbering, at least six running digits per Annex G; blueprint/checklist use 1 + 15 digit rule for OR and 2 + 15 digit rule for void OR. Reset counter may append to running serial. | Numbering model must be configurable and BIR-reviewed. It must be site/POS-server scoped and support separate sequences for invoices/receipts and adjustment documents. Numbering details are unresolved because sources differ in specificity. |
| Reset counter | BIR blueprint; Annex E-1; Annex G | Reset counter is reported in BIR Sales Summary and may be appended to document numbers; non-volatile memory stores reset counter. | Site POS Server must own reset counter state. If implemented on server rather than physical APM, non-resettable persistence and recovery semantics must be defined. |
| Grand accumulated sales | BIR blueprint; Annex E-1; Hikvision checklist | Non-resettable grand total accumulator; Annex E-1 includes Grand Accumulated Sales ending and beginning balances. | POS Server requires immutable fiscal accumulator state, including beginning/ending values per BIR sales summary and Z-close. |
| X-Reading | Annex D-1; Annex G; BIR blueprint; Hikvision gap analysis/checklist | X-Reading is cashier accountability / partial-day snapshot since last Z. Must include title, cashier/operator, period, document range, transaction counts, sales/tenders, void/refund amounts, VAT summary. | X-read is a POS Server responsibility and should be available per terminal/session and aggregated at site-level as required. Printed format should be simplified to Annex D-1 style; detailed metadata remains in digital records. |
| Z-Reading | Annex D-2; Annex G; BIR blueprint; Hikvision gap analysis/checklist | Z-Reading is end-of-day report; Z-counter advances by one per generated Z; closes business day; includes beginning/ending numbers, void ranges, gross/net/VAT breakdown, deductions, tender summary, reset counter, grand total. | Z-close must be atomic at Site POS Server, with channel/terminal cutover controls. Central PMS payment finality must feed but not own Z-close. |
| BIR Sales Summary | Annex E-1; Annex F via gap analysis; Hikvision gap analysis | Annex E-1 requires BIR Sales Summary with beginning/ending SI/OR numbers, grand accumulated balances, manual SI/OR sales, gross sales, VATable, VAT, exempt, zero-rated, deductions, VAT adjustments, net sales, total income, reset counter, Z-counter, remarks. | BIR Sales Summary is a required fiscal report, not merely a backend analytics export. It must reconcile to Z-read and fiscal document sequences. |
| Statutory sales books | Annex E-2 to E-5; Annex G; v1.2 operator console/statutory discount docs | E-2 Senior Citizen, E-3 PWD, E-4 National Athletes and Coaches, E-5 Solo Parent reports include beneficiary identifiers, SI/OR numbers, sales, VAT, discounts, net sales or equivalent fields. | POS/Invoicing must consume approved statutory discount outcomes and produce statutory sales books. v1.2 covers Senior/PWD policy workflows; NAAC and Solo Parent support appears to be a gap. |
| Electronic Journal | BIR blueprint; Annex G; Hikvision checklist/gap analysis | E-journal must replicate OR/SI, void/refund/return, X-reading, Z-reading in soft copy; retained 10 years; every document/event appears once; exportable. | POS Server needs immutable EJ ledger for all fiscal events and reports. EJ entries must link to Central PMS payment attempt/confirmation without making Central PMS the fiscal ledger. |
| POSLog | BIR blueprint; Hikvision checklist/gap analysis | POSLog should use ARTS POSLog 6.x/7.x with retail transaction, line item, tender, tax, totals, plus BIR and vehicle extensions. Must reconcile with receipt, EJ, X/Z. | POSLog is a fiscal/integration export owned by POS Server. Parking domain fields should be extension fields, not repurposed retail fields. |
| Reprint controls | Hikvision gap analysis | Sales Invoice, X-Read, Z-Read, and EJ reprints must be supported; reprints must be labeled and logged. | POS Server must maintain original fiscal documents and generate controlled reprints without mutating originals. Reprint audit trail is required. |
| Void, cancel, refund, return | Annex G; BIR blueprint; Hikvision gap analysis | Adjustment documents must have their own numbers, reference original transactions, present original values in negative form, include non-input-tax warning, and be restricted/admin controlled. | POS Server owns void/refund/cancel fiscal controls. Central PMS still owns payment reversal/refund finality where provider money movement is involved. The cross-service sequence is unresolved. |
| Online/offline indicator | Annex F via Hikvision gap analysis | Administrative interface must display online/offline state. | Site POS Server and terminals/channels need health state, offline queue state, and fiscal risk indicators. Offline fiscal issuance policy is unresolved. |
| Security and anti-tampering | BIR blueprint; Hikvision checklist; Annex F category implied | No transaction deletion; posted transactions cannot be edited; hash/integrity on reports; clock rollback prevention; export logging; supervisor PIN/admin controls. | POS Server needs append-only fiscal persistence, immutable document snapshots, privileged controls, clock policy, and auditable exports. |
| Retention and backup | BIR blueprint; Hikvision checklist | 10-year retention for BIR-relevant files, reports, EJ, receipts; backups must include hashes and preserve EJ continuity. | Fiscal storage retention differs from current operational TTL patterns and must be treated as compliance storage. |
| Source code and accreditation package | BIR blueprint; Hikvision checklist | Source code documentation, manuals, schemas, samples, anti-tamper design, backup/DR documentation, operator/admin manuals. | v1.3 planning must include accreditation evidence package outputs, not only runtime capabilities. |

## Existing ExitPass v1.2 Baseline

| Existing capability | v1.2 source signal | POS/Invoicing relevance |
| --- | --- | --- |
| Sites, site groups, lanes, devices | v1.2 DDL includes `sites.site_groups`, `sites.sites`, `sites.lanes`, device assignment and gate device site links. | POS Server should resolve from existing site boundary and should not create an unrelated location model. |
| Parking sessions | v1.2 DDL includes `core.parking_sessions` with `site_group_id`, `site_id`, `vendor_system_id`, vendor session reference, entry data, status. | Fiscal issuance should bind to canonical parking session and resolved site. |
| Tariff snapshots | v1.2 DDL includes `core.tariff_snapshots` with gross amount, statutory/coupon discounts, payable amount. | Fiscal line/tax computation needs a stable payable basis; gaps remain around VAT/exempt details and itemized fiscal lines. |
| Payment attempts and confirmations | v1.2 DDL and services define payment attempt creation/reuse and recorded payment confirmation. | Central PMS remains payment finality authority. POS Server should issue only after Central PMS confirmation or an approved pending/offline policy. |
| ExitAuthorization | v1.2 DDL and services define issue/consume authorization after confirmed payment. | ExitAuthorization remains Central PMS/gate integration responsibility, not POS fiscal responsibility. |
| WebPay and PaymentOrchestrator | Existing services create WebPay payment intents and provider sessions/outcomes. | WebPay becomes a channel under Site POS Server for fiscal issuance, not a separate fiscal source of truth. |
| Operator Console | v1.2 operator docs cover site/device/shift validation, statutory discount validation, audit, and reporting. | Cashier/operator-assisted POS can reuse concepts for operator identity, shift/site validation, and audit accountability, but fiscal cashier/session accountability is broader than statutory discount validation. |
| Audit infrastructure | v1.2 DDL includes audit schemas, audit events, trail entries, security events, evidence links. | POS Server fiscal audit may reuse common audit patterns, but fiscal ledger/EJ immutability and 10-year retention likely require dedicated fiscal storage semantics. |

## Gaps Identified

| Gap | Source basis | Planning note |
| --- | --- | --- |
| No explicit POS Server domain in v1.2. | v1.2 DDL/services expose payment/session/gate domains but no fiscal document, POS server, fiscal terminal, X/Z, EJ, POSLog, or BIR summary tables/services. | v1.3 needs a new domain boundary or service/module for Site POS Server. |
| Fiscal document type is unresolved. | Hikvision gap says switch OR to Sales Invoice; Annex G allows SI or OR depending on transaction type. | Legal/accounting decision required before final BRD. |
| Site-level POS Server vs physical machine accreditation is unresolved. | BIR/Hikvision sources focus on Autopay station hardware/machine; user architecture requires Site-level POS Server with channels/terminals. | Need BIR/accountant confirmation whether server-level fiscal machine identity is acceptable, how terminal serial/MIN/PTU maps, and whether APM hardware remains separately accredited. |
| Tax/VAT detail is insufficient in current v1.2 tariff snapshot. | Annex E and receipts require VATable, VAT amount, exempt, zero-rated, discounts. | Need tax calculation/source-of-truth design for parking services, discounts, and channel-specific tenders. |
| NAAC and Solo Parent reporting are not represented in v1.2 statutory model. | Annex E-4/E-5 require reports; v1.2 baseline emphasizes Senior/PWD. | Add open requirements or explicitly defer if not applicable. |
| Offline issuance policy is unresolved. | Annex F gap asks for online/offline indicator; BIR sources require no gaps and immutable counters. | Need rule for offline terminals, APM connectivity loss, and central POS Server unavailability. |
| Void/refund split between fiscal and payment domains is unresolved. | Annex G/Hikvision require adjustment documents; Central PMS owns payment finality. | Need cross-service workflow: provider refund/reversal, fiscal adjustment document, gate/exit state impact, reconciliation. |
| Reset and grand total non-resettable behavior needs platform interpretation. | BIR blueprint expects non-volatile hardware behavior. | Need architecture for database-backed non-resettable counters, backup/restore, DR, and tamper-evidence. |

## Inputs To Carry Into BRD Later

- POS/Invoicing is a platform-wide capability.
- Site resolution determines fiscal POS Server.
- POS Server is the fiscal authority for document numbering, counters, X/Z, BIR summaries, fiscal audit, cashier/session accountability, void/refund fiscal controls, and reporting.
- Central PMS remains authority for payment finality and ExitAuthorization.
- WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future channels are terminals/channels under a site POS Server.
- Source-specific unresolved items must stay visible in the BRD backlog until resolved by business, legal/accounting, or BIR accreditation review.
