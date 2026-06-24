# ExitPass POS Server Impact Map

Version: v1.3 initial planning artifact
Status: Draft for planning only
Generated: 2026-06-24

## Architectural Target

The Site-level POS Server is the fiscal authority for one site or parking operation boundary. It issues fiscal documents for all payment channels after the site is resolved. Channels and terminals include WebPay, AutoPay Machine, Cashier POS, EC Device, operator-assisted payment, and future channels.

Central PMS remains authority for parking session control, payment finality, and ExitAuthorization.

## Authority Split

| Capability | Central PMS | Site POS Server | Channel / terminal |
| --- | --- | --- | --- |
| Parking session canonical identity | Owns | References | Supplies lookup/session context |
| Site resolution | Owns or provides authoritative result | Consumes resolved site | Supplies terminal/channel context |
| Tariff/payable basis | Owns current v1.2 tariff snapshots; may need tax detail extension | Consumes fiscal line/tax basis | Displays amount or captures tender |
| Payment attempt | Owns | References | Initiates through Central PMS/payment services |
| Payment finality | Owns | Consumes confirmed payment event/context | Displays status |
| Provider outcome truth | Owns | References for fiscal tender evidence | Captures or displays provider references |
| Invoice/receipt issuance | Does not own | Owns | Requests/prints/displays |
| Invoice/receipt numbering | Does not own | Owns | Shows assigned number |
| Reset counter | Does not own | Owns | Shows/report only |
| Grand accumulated fiscal sales | Does not own | Owns | N/A |
| X-read | Does not own | Owns | May request terminal/cashier X-read |
| Z-read | Does not own | Owns | May participate in close/cutover |
| BIR Sales Summary | Does not own | Owns | N/A |
| Annex E sales books | Provides discount/payment/session facts | Owns report generation | Captures channel/operator details if needed |
| Void/refund fiscal documents | Payment reversal/refund finality | Fiscal adjustment document and report effect | Operator action or customer flow trigger |
| ExitAuthorization | Owns | Does not own | Displays/uses authorization state only |
| Cashier/session accountability | Existing operator/device/shift concepts | Owns fiscal cashier/session close accountability | Captures cashier/terminal events |
| Fiscal audit trail/EJ/POSLog | References fiscal IDs if needed | Owns | Emits terminal events into POS Server |

## Proposed Logical Components

| Component | Responsibility | Source basis | Existing v1.2 relation |
| --- | --- | --- | --- |
| POS Server Registry | Maps site/operation boundary to POS Server fiscal profile, taxpayer branch, PTU/MIN/accreditation metadata, active status. | Annex G header/footer fields; user site-level architecture. | Extends existing site/site-group model. |
| POS Terminal Registry | Registers WebPay, APM, Cashier POS, EC Device, operator-assisted, and future terminals/channels under POS Server. | Annex G POS terminal and machine fields; BIR blueprint terminal/file structure. | Could relate to site devices, lanes, service identities, and WebPay service identities. |
| Fiscal Document Service | Issues Sales Invoice/Official Receipt/adjustment documents with immutable snapshots, numbering, and renderable layouts. | Annex G, BIR blueprint, Hikvision gap/checklist. | New capability. References `parking_session_id`, `payment_attempt_id`, `payment_confirmation_id`, tariff snapshot, site. |
| Numbering and Counter Service | Owns document sequences, reset counter, Z-counter, grand accumulated sales, first/last document numbers. | Annex E-1, Annex G, BIR blueprint. | New capability. Must not be implemented as incidental application counters. |
| X/Z Reporting Service | Generates X-Reading and Z-Reading outputs, closes fiscal day, and stores report hashes/snapshots. | Annex D-1, Annex D-2, Annex G, BIR blueprint. | New capability. Needs coordination with Central PMS for finalized sales window. |
| BIR Sales Summary Service | Produces Annex E-1 BIR Sales Summary and reconciles to Z-read and fiscal documents. | Annex E-1, Annex F via Hikvision gap. | New capability. |
| Statutory Sales Book Service | Produces Annex E-2 to E-5 reports for Senior, PWD, NAAC, and Solo Parent. | Annex E-2 to E-5; Annex G. | Senior/PWD can reference v1.2 statutory validation; NAAC/Solo Parent appear as gaps. |
| Electronic Journal Service | Maintains complete sequential ledger/replica of fiscal documents, adjustment documents, X/Z reports, and exports. | Annex G, BIR blueprint, Hikvision checklist. | New fiscal ledger; may reuse audit patterns but should remain distinct from general audit. |
| POSLog Export Service | Produces ARTS POSLog with BIR and vehicle extensions and reconciles to EJ/documents. | BIR blueprint, Hikvision checklist. | New export capability. |
| Fiscal Audit and Reprint Controls | Logs fiscal administrative actions, reprints, exports, void/refund/cancel, Z-close, configuration changes. | Annex G audit trail; Hikvision gap/checklist. | Can reuse v1.2 audit infrastructure but needs fiscal-specific events and retention. |
| Fiscal Storage and Retention | Stores immutable payloads, rendered documents, hashes, export files, and retention policies. | BIR blueprint 10-year retention and backup rules. | New or extended storage policy. |

## Channel Impact

| Channel / terminal | Required impact | Key open issue |
| --- | --- | --- |
| WebPay | Must request or receive fiscal issuance after Central PMS confirms payment. Must display/download/print invoice/receipt tied to resolved site POS Server. | WebPay has no physical terminal serial/printer. Need fiscal terminal identity and print/download policy. |
| AutoPay Machine | Must become one terminal/channel under the site POS Server. APM printer may print POS Server-issued document or APM may locally render a server-issued payload. | BIR machine/MIN/PTU mapping between Site POS Server and physical APM is unresolved. |
| Cashier POS | Must support cashier login/session, cash/tender capture, reprint/void/refund controls, X-read and cashier accountability. | Need role/shift integration and cash drawer/session rules. |
| EC Device | Must be modeled as a terminal/channel if it accepts or confirms payment. | Need definition of EC device fiscal identity and print/display requirements. |
| Operator-assisted payment | Must flow through Central PMS finality and Site POS Server issuance, with operator identity included in fiscal audit where applicable. | Need distinguish assistance from cashier fiscal responsibility. |
| Future channels | Must register as terminals/channels under POS Server rather than creating separate fiscal authorities. | Need extensible terminal/channel taxonomy. |

## Data And Contract Impact

| Area | Needed by POS Server | Current v1.2 source / gap |
| --- | --- | --- |
| Site routing | `site_id`, `site_group_id`, taxpayer/branch fiscal profile, POS Server ID. | v1.2 parking sessions and site tables already carry site scope. POS Server mapping is missing. |
| Fiscal identity | POS Server serial/MIN/PTU, terminal number, supplier accreditation fields, software version. | Required by Annex G/BIR blueprint. Missing in v1.2 fiscal context. |
| Payment context | Payment attempt, confirmation, provider transaction reference, rail/tender type, confirmed amount, timestamps. | v1.2 payment attempts/confirmations/provider outcomes exist. Fiscal tender normalization may need more fields. |
| Fiscal line basis | Parking fee lines, lost ticket fees, penalties, surcharges, service charges, discounts, coupons, tax classification. | v1.2 tariff snapshots cover gross/discount/payable but not full fiscal tax line detail. |
| Customer/buyer fields | Buyer name/address/TIN/business style when required. | Annex G requires provision for customer/buyer details for VAT OR/SI. v1.2 customer capture is not evident for parking flows. |
| Statutory report identity fields | Senior/PWD/NAAC/Solo Parent names, IDs, TINs or other report fields. | v1.2 privacy posture minimizes data. Need compliance/privacy decision. |
| Fiscal document state | Issued, reprinted, voided/cancelled/refunded/returned, linked original/adjustment docs. | Missing in v1.2. |
| Counter state | Document sequences, reset counter, Z-counter, grand totals, first/last numbers. | Missing in v1.2. |
| Fiscal reports | X-read, Z-read, BIR Sales Summary, Annex E sales books, EJ, POSLog. | Missing in v1.2. |

## Service Interaction Sketch

1. Channel resolves or receives a parking session through existing Central PMS flows.
2. Central PMS owns payment attempt and payment finality.
3. After confirmed finality, Central PMS or PaymentOrchestrator sends fiscal issuance request/context to the resolved Site POS Server.
4. Site POS Server validates site/POS Server/terminal status, fiscal day status, numbering availability, and idempotency.
5. Site POS Server issues fiscal document, assigns number, updates fiscal counters, writes EJ/POSLog/audit records, and returns fiscal document identity/status.
6. Channel displays, prints, or provides the document according to terminal capability.
7. Central PMS issues ExitAuthorization only according to its existing finality and authorization rules.

Open choreography issue: whether ExitAuthorization waits for successful fiscal issuance, proceeds in parallel after finality, or can proceed under defined fiscal issuance retry/exception states.

## Risks

| Risk | Why it matters | Mitigation planning note |
| --- | --- | --- |
| Treating APM as the only fiscal scope | Would exclude WebPay/cashier/operator-assisted channels from BIR-compliant fiscal issuance. | Keep Site POS Server as source BRD architecture. |
| Letting Central PMS issue invoices directly | Blurs payment finality and fiscal authority boundaries. | POS Server owns fiscal issuance; Central PMS references fiscal IDs. |
| Document sequence gaps | BIR sources require sequential/no-gap behavior and adjustment logging. | Design idempotent fiscal issuance and failed-reservation handling. |
| Offline terminal issuance | Could create duplicate numbers or unreconciled sales. | Define offline policy before implementation. |
| Incomplete tax basis | Annex E and receipts require detailed VAT/exempt/zero-rated/deduction values. | Add tax/fiscal line model before final BRD. |
| Privacy conflict in Annex E reports | Sales books may require personal data; v1.2 privacy design minimizes it. | Legal/privacy review before storing or rendering personal details. |
| Server DR counter reset | Reset/Z/grand total integrity may be challenged if restored incorrectly. | Define tamper-evident fiscal state store and DR procedure. |

## Not In This Artifact

- No database schema changes.
- No source code changes.
- No final BRD.
- No DOCX output.
