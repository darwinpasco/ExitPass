# ExitPass POS/Invoicing Source Analysis

Version: v1.3 decision update planning artifact
Status: Draft for planning only
Generated: 2026-06-25

## Scope Boundary

This is not the v1.3 BRD. This document identifies source-driven requirements and planning implications for a platform-wide ExitPass POS/Invoicing capability after review decisions.

ExitPass POS/Invoicing must be modeled as a site-level fiscal capability, not as a Hikvision AutoPay Machine-only requirement. BIR recommended the Site-level POS Server model. Each Site or parking operation boundary resolves to one Site-level POS Server. WebPay, AutoPay Machine, Cashier POS, EC Device, operator-assisted payment, and future payment channels are modeled as channels or terminals under that Site-level POS Server.

The resolved Site determines which POS Server issues the Sales Invoice.

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
| Diplomat VAT Privilege / VAT Exemption | BIR Revenue Memorandum Order No. 10-2019, per review instruction |
| Existing ExitPass v1.2 documents | `D:\Docs\ExitPass\v1.2\*`; repo v1.2 DDL and existing docs under `docs/` |

Note: the RMO main PDF appears image/scanned in the prior local extraction environment, and Annex D/F PDFs were not fully text-extractable using available local tools. Requirements attributed to those sources are based on the file names, the consolidated BIR blueprint, the Annex G text, the Annex E workbook extraction, and the Hikvision examiner gap analysis where those annexes are explicitly referenced. Exact field-by-field PDF validation remains an open layout question.

## Platform Position

| Position | Planning implication |
| --- | --- |
| POS/Invoicing is platform-wide. | Do not bind fiscal issuance only to Hikvision APM. WebPay, APM, cashier, EC device, operator-assisted, and future channels all issue through the resolved Site-level POS Server. |
| BIR recommended the Site-level POS Server model. | Do not model each payment channel as a separate independent POS system. |
| One Site-level POS Server per Site or parking operation boundary. | The POS Server is the fiscal authority for numbering, counters, reports, and fiscal audit data for that Site. Terminals/channels are children of the server. |
| Parking payment fiscal output is Sales Invoice. | ExitPass v1.3 primary parking payment output is Sales Invoice. Other fiscal document types remain design considerations for adjustments or other transaction types. |
| Central PMS owns payment finality and ExitAuthorization. | POS Server must not finalize payment or issue gate authorization. |
| Fiscal issuance must succeed before ExitAuthorization. | Central PMS requests Sales Invoice issuance after verified payment finality, records the fiscal reference, and only then issues ExitAuthorization. |
| POS Server owns fiscal issuance. | Fiscal issuance includes Sales Invoice numbering, reset counter, X-read, Z-read, BIR sales summary, fiscal adjustment controls, cashier/session accountability, audit trail, EJ, POSLog, and fiscal reporting. |

## Source-Derived Requirement Map

| Requirement area | Source category | Requirement signal | Planning interpretation after review |
| --- | --- | --- | --- |
| Fiscal document terminology | Hikvision gap analysis; Annex G; BIR guidance | Examiner gap says Official Receipt must be changed to Sales Invoice; Annex G requires document title by applicable fiscal document. | Decided: primary parking payment fiscal output for ExitPass v1.3 is Sales Invoice. Adjustment documents, void/refund/cancel documents, credit memo/debit memo equivalents, or other BIR-required fiscal documents remain design considerations. |
| Site POS Server model | BIR review guidance; user decision; v1.2 site model | BIR recommended Site-level POS Server model. | Decided: Site POS Server is fiscal authority for the resolved Site. Payment channels are terminals/channels, not separate POS systems. |
| Invoice required header fields | Annex G; BIR blueprint; Hikvision checklist | Business name, address, TIN, VAT/non-VAT TIN, MIN, serial number, software name/version, POS terminal number, transaction date/time, document number. | Site POS Server needs taxpayer/site fiscal profile and Sales Invoice document identity. Exact MIN/PTU/serial/software/accreditation assignment between server and terminals/channels remains open. |
| Footer and supplier accreditation fields | Annex G; Annex F via gap analysis; Hikvision gap analysis | Footer must include software supplier name/address/TIN, accreditation number/date/validity, PTU or ATG number/date/validity. | POS Server profile must support supplier accreditation and PTU metadata. Assignment across Site POS Server, APM, cashier, EC/continuity, WebPay, and operator-assisted channel remains open. |
| Document numbering | BIR blueprint; Hikvision checklist; Annex G | BIR-approved numbering, running serial, reset counter, adjustment documents. | Sales Invoice output is decided. Exact Sales Invoice and adjustment numbering patterns remain open for BIR/accounting confirmation. |
| Reset counter | BIR blueprint; Annex E-1; Annex G; review decision | Reset counter is reported in BIR Sales Summary and may be part of numbering; non-volatile memory stores reset counter. | Decided: reset counter starts at zero and increments only for fiscal reset events. POS Server preserves previous Grand Total Amount, previous reset counter, reset timestamp, reason, approving user, and recovery/reference notes. |
| Z-counter | Annex D-2; Annex E-1; Annex G; review decision | Z-counter advances every time Z-Reading is generated. | Decided: Z-counter is separate from reset counter and advances per Z-reading / fiscal day close. |
| Grand accumulated sales | BIR blueprint; Annex E-1; Hikvision checklist; review decision | Non-resettable grand total accumulator; Annex E-1 includes Grand Accumulated Sales ending and beginning balances. | POS Server must preserve last Grand Total Amount and prove it was not rolled back after reset, restore, failover, repair, or recovery. |
| X-Reading | Annex D-1; Annex G; BIR blueprint; Hikvision gap/checklist | X-Reading is cashier accountability / partial-day snapshot since last Z. | X-read remains POS Server responsibility, with terminal/cashier/session views as needed. Exact aggregation remains open. |
| Z-Reading | Annex D-2; Annex G; BIR blueprint; Hikvision gap/checklist | Z-Reading is end-of-day report and Z-counter advances by one. | Z-close is POS Server fiscal day close, distinct from reset. Z-close must not increment reset counter. |
| BIR Sales Summary | Annex E-1; Annex F via gap analysis; Hikvision gap analysis | Annex E-1 requires beginning/ending SI/OR numbers, grand accumulated balances, manual SI/OR sales, gross sales, VATable, VAT, exempt, zero-rated, deductions, VAT adjustments, net sales, total income, reset counter, Z-counter, remarks. | BIR Sales Summary is a first-class fiscal report and must reconcile to Sales Invoice sequence, fiscal lines, reset counter, Z-counter, and grand totals. |
| Entitlement sales books | Annex E-2 to E-5; Annex G; v1.2 operator console/statutory discount docs | E-2 Senior Citizen, E-3 PWD, E-4 NAAC, E-5 Solo Parent reports include beneficiary identifiers, SI/OR numbers, sales, VAT, discounts, net sales or equivalent fields. | Senior/PWD are immediate operational workflows. NAAC and Solo Parent are future-supported categories that the fiscal model must accommodate later. |
| Diplomat VAT Privilege / VAT Exemption | RMO No. 10-2019 per review instruction | Diplomat VAT Privilege / VAT Exemption is already in effect. | Must be modeled as an active VAT privilege / VAT exemption entitlement, not an ordinary commercial discount. Exact evidence, scope, wording, reporting, and retention remain open. |
| Fiscal line classification | Annex E; Annex G; BIR blueprint; review decision | Sales summaries, Sales Invoice layout, EJ, POSLog, and audit require classification of sales, tax, discounts, and adjustments. | POS Server must eventually have explicit fiscal lines or fiscal classification records. Do not bury tax treatment only inside tariff snapshots. |
| Electronic Journal | BIR blueprint; Annex G; Hikvision checklist/gap analysis | E-journal must replicate SI, void/refund/return, X-reading, Z-reading in soft copy; retained; every document/event appears once; exportable. | POS Server needs immutable EJ ledger linked to Central PMS payment references without making Central PMS fiscal ledger owner. |
| POSLog | BIR blueprint; Hikvision checklist/gap analysis | POSLog should use ARTS POSLog 6.x/7.x with retail transaction, line item, tender, tax, totals, plus BIR and vehicle extensions. | POSLog is a POS Server fiscal/integration export. It must reconcile with Sales Invoice, EJ, X/Z, and BIR Sales Summary. |
| Fiscal issuance before ExitAuthorization | Review decision; v1.2 Central PMS authority | Central PMS owns payment finality and ExitAuthorization. | Final choreography: payment finality, Sales Invoice issuance, fiscal reference recording, ExitAuthorization. If issuance fails, no ExitAuthorization yet. |
| Reprint controls | Hikvision gap analysis | Sales Invoice, X-Read, Z-Read, and EJ reprints must be supported, labeled, and logged. | Reprints are controlled fiscal actions and must not mutate original fiscal records. |
| Void, cancel, refund, return | Annex G; BIR blueprint; Hikvision gap analysis | Adjustment documents must have their own numbers, reference original transactions, present original values in negative form, and be restricted/admin controlled. | POS Server owns fiscal adjustment documents. Central PMS still owns payment reversal/refund finality. Workflow sequencing remains open. |
| Online/offline indicator | Annex F via Hikvision gap analysis | Administrative interface must display online/offline state. | Site POS Server and terminals/channels need health state. Offline fiscal issuance policy remains open. |
| DR/restore and counter integrity | BIR blueprint; Hikvision checklist; review decision | No rollback, no deletion, hash/integrity on reports, backup continuity, anti-tamper behavior. | POS Server must use tamper-evident, append-only fiscal state and never resume from lower externally anchored fiscal state. Exact implementation is System Design. |

## Existing ExitPass v1.2 Baseline

| Existing capability | v1.2 source signal | POS/Invoicing relevance |
| --- | --- | --- |
| Sites, site groups, lanes, devices | v1.2 DDL includes `sites.site_groups`, `sites.sites`, `sites.lanes`, device assignment and gate device site links. | POS Server should resolve from existing Site boundary and should not create an unrelated location model. |
| Parking sessions | v1.2 DDL includes `core.parking_sessions` with `site_group_id`, `site_id`, `vendor_system_id`, vendor session reference, entry data, status. | Sales Invoice issuance must bind to canonical parking session and resolved Site. |
| Tariff snapshots | v1.2 DDL includes `core.tariff_snapshots` with gross amount, statutory/coupon discounts, payable amount. | Tariff snapshots are not sufficient alone. POS Server needs explicit fiscal lines/classification records for VATable, exempt, zero-rated, non-VAT, privileges, discounts, coupons, penalties, lost tickets, overstay, service charges, and adjustments. |
| Payment attempts and confirmations | v1.2 DDL and services define payment attempt creation/reuse and recorded payment confirmation. | Central PMS remains payment finality authority. Sales Invoice issuance occurs after verified payment finality and before ExitAuthorization. |
| ExitAuthorization | v1.2 DDL and services define issue/consume authorization after confirmed payment. | ExitAuthorization remains Central PMS/gate integration responsibility, and waits for successful fiscal issuance reference. |
| WebPay and PaymentOrchestrator | Existing services create WebPay payment intents and provider sessions/outcomes. | WebPay becomes a channel under Site POS Server for Sales Invoice issuance. WebPay and Payment Orchestrator must not bypass Central PMS. |
| Operator Console | v1.2 operator docs cover site/device/shift validation, statutory discount validation, audit, and reporting. | Cashier/operator-assisted POS can reuse operator identity, shift/site validation, and audit concepts, but fiscal cashier/session accountability is broader. |
| Audit infrastructure | v1.2 DDL includes audit schemas, audit events, trail entries, security events, evidence links. | POS Server fiscal audit may reuse common audit patterns, but fiscal ledger/EJ immutability and retention require dedicated fiscal semantics. |

## Gaps Identified

| Gap | Source basis | Updated planning note |
| --- | --- | --- |
| No explicit POS Server domain in v1.2. | v1.2 DDL/services expose payment/session/gate domains but no fiscal document, POS server, fiscal terminal, X/Z, EJ, POSLog, or BIR summary tables/services. | v1.3 needs a Site POS Server domain boundary or service/module. |
| MIN/PTU/serial/software/accreditation assignment is unresolved. | Annex G and BIR blueprint require machine/terminal/supplier/PTU fields. | Site-level POS Server is accepted; remaining open question is assignment across POS Server and each terminal/channel type. |
| Tax/VAT detail is insufficient in current v1.2 tariff snapshot. | Annex E and Sales Invoice outputs require VATable, VAT amount, exempt, zero-rated, non-VAT, discounts, privileges, and adjustments. | Need explicit fiscal line/classification model. Exact tax treatment remains finance/accounting configuration. |
| NAAC and Solo Parent operational workflows are not active. | Annex E-4/E-5 require report structures; v1.2 baseline emphasizes Senior/PWD. | Treat NAAC and Solo Parent as future-supported categories that fiscal model can accommodate. |
| Diplomat VAT Privilege / VAT Exemption must be supported. | RMO No. 10-2019 per review instruction. | Treat as active VAT privilege / VAT exemption entitlement, not ordinary discount. |
| Offline issuance policy is unresolved. | Annex F gap asks for online/offline indicator; BIR sources require no gaps and immutable counters. | Need rule for offline terminals, APM connectivity loss, and central POS Server unavailability. |
| Void/refund split between fiscal and payment domains is unresolved. | Annex G/Hikvision require adjustment documents; Central PMS owns payment finality. | Need cross-service workflow for provider refund/reversal, fiscal adjustment document, gate/exit state impact, and reconciliation. |
| DR/restore implementation is unresolved. | BIR blueprint expects non-volatile/tamper-evident behavior. | Requirement is accepted: never resume from lower fiscal state. Exact implementation is a System Design item. |

## Inputs To Carry Into BRD Later

- POS/Invoicing is a platform-wide capability.
- Parking payment fiscal output for v1.3 is Sales Invoice.
- BIR recommended the Site-level POS Server model.
- Site resolution determines fiscal POS Server.
- POS Server is the fiscal authority for Sales Invoice issuance, document numbering, counters, X/Z, BIR summaries, fiscal audit, cashier/session accountability, adjustment fiscal controls, and reporting.
- Central PMS remains authority for payment finality and ExitAuthorization.
- Fiscal issuance must succeed before Central PMS issues ExitAuthorization.
- WebPay, APM, Cashier POS, EC Device, operator-assisted payment, and future channels are terminals/channels under a Site POS Server.
- Reset counter starts at zero and increments only per fiscal reset; Z-counter advances per Z-reading / fiscal day close.
- Senior Citizen and PWD are immediate operational entitlement workflows.
- NAAC and Solo Parent are future-supported categories.
- Diplomat VAT Privilege / VAT Exemption is an active VAT privilege / VAT exemption entitlement category.
- Exact fiscal identity, tax treatment, Diplomat implementation details, offline policy, and DR mechanism remain open and must stay visible.
