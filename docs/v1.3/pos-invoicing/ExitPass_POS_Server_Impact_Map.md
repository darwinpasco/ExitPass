# ExitPass POS Server Impact Map

Version: v1.3 decision update planning artifact
Status: Draft for planning only
Generated: 2026-06-25

## Architectural Target

The Site-level POS Server is accepted as the fiscal authority for one Site or parking operation boundary. It issues Sales Invoices for parking payments across all payment channels after the Site is resolved. Channels and terminals include WebPay, AutoPay Machine, Cashier POS, EC Device / Continuity Terminal, operator-assisted payment, and future channels.

Central PMS remains authority for parking session control, payment finality, and ExitAuthorization. Fiscal issuance must complete successfully before Central PMS issues ExitAuthorization.

## Authority Split

| Capability | Central PMS | Site POS Server | Channel / terminal |
| --- | --- | --- | --- |
| Parking session canonical identity | Owns | References | Supplies lookup/session context |
| Site resolution | Owns or provides authoritative result | Consumes resolved Site and maps to fiscal POS Server | Supplies terminal/channel context |
| Tariff/payable basis | Owns current v1.2 tariff snapshots; may need tax/fiscal detail handoff | Consumes and classifies fiscal lines | Displays amount or captures tender |
| Payment attempt | Owns | References | Initiates through Central PMS/payment services |
| Payment finality | Owns | Consumes confirmed payment event/context | Displays status |
| Provider outcome truth | Owns | References for fiscal tender evidence | Captures or displays provider references |
| Sales Invoice issuance | Does not own | Owns | Requests/prints/displays |
| Sales Invoice numbering | Does not own | Owns | Shows assigned number |
| Reset counter | Does not own | Owns; starts at zero and increments only per fiscal reset | Shows/report only |
| Z-counter | Does not own | Owns; advances per Z-reading / fiscal day close | Shows/report only |
| Grand Total Amount | Does not own | Owns fiscal accumulator and audit reference | N/A |
| X-read | Does not own | Owns | May request terminal/cashier X-read |
| Z-read | Does not own | Owns | May participate in close/cutover |
| BIR Sales Summary | Does not own | Owns | N/A |
| Entitlement sales books | Provides discount/payment/session facts | Owns fiscal report generation | Captures channel/operator details if needed |
| Void/refund/cancel fiscal documents | Owns payment reversal/refund finality | Owns fiscal adjustment document and report effect | Operator action or customer flow trigger |
| ExitAuthorization | Owns; issued only after fiscal issuance succeeds | Does not own | Displays/uses authorization state only |
| Cashier/session accountability | Existing operator/device/shift concepts | Owns fiscal cashier/session accountability | Captures cashier/terminal events |
| Fiscal audit trail/EJ/POSLog | References fiscal IDs if needed | Owns | Emits terminal events into POS Server |

## Payment-To-Exit Choreography

1. Central PMS receives verified payment finality.
2. Central PMS requests Sales Invoice issuance from the resolved Site POS Server.
3. POS Server successfully issues the Sales Invoice and returns fiscal document identity/status.
4. Central PMS records the fiscal issuance reference.
5. Central PMS issues ExitAuthorization.

If Sales Invoice issuance fails or times out:

- Payment finality is not reversed automatically.
- ExitAuthorization is not issued yet.
- The case enters a controlled fiscal issuance exception/retry workflow.
- Customer/operator messaging must show that payment was received but fiscal issuance is pending and exit authorization is not yet available.
- Manual release, if allowed, must be supervisor-approved, incident-tagged, and reconciliation-tagged.
- POS Server must not issue ExitAuthorization.
- Payment Orchestrator and WebPay must not bypass Central PMS.

## Proposed Logical Components

| Component | Responsibility | Source basis | Existing v1.2 relation |
| --- | --- | --- | --- |
| POS Server Registry | Maps Site/operation boundary to POS Server fiscal profile, taxpayer branch, PTU/MIN/accreditation metadata, active status. | BIR-recommended Site-level model; Annex G header/footer fields. | Extends existing site/site-group model. |
| POS Terminal Registry | Registers WebPay, APM, Cashier POS, EC Device / Continuity Terminal, operator-assisted, and future terminals/channels under POS Server. | Annex G POS terminal and machine fields; BIR blueprint terminal/file structure. | Could relate to site devices, lanes, service identities, and WebPay service identities. |
| Fiscal Document Service | Issues Sales Invoice for parking payments and adjustment fiscal documents as needed, with immutable snapshots, numbering, and renderable layouts. | BIR guidance, Annex G, BIR blueprint, Hikvision gap/checklist. | New capability. References `parking_session_id`, `payment_attempt_id`, `payment_confirmation_id`, tariff/fiscal line basis, and Site. |
| Fiscal Line Classification Service | Classifies fiscal lines for VATable, VAT-exempt, zero-rated, non-VAT, statutory discounts, VAT privileges/exemptions, coupons, penalties, lost ticket fees, overstay charges, service charges, and other fiscal adjustments. | Annex E, Sales Invoice layout, EJ, POSLog, audit requirements. | New capability. Must not bury fiscal tax treatment only inside tariff snapshots. |
| Numbering and Counter Service | Owns Sales Invoice sequences, adjustment document sequences, reset counter, Z-counter, Grand Total Amount, first/last document numbers, and counter continuity checks. | Annex E-1, Annex G, BIR blueprint, review decisions. | New capability. Must not be implemented as incidental application counters. |
| X/Z Reporting Service | Generates X-Reading and Z-Reading outputs, closes fiscal day, stores report hashes/snapshots, and separates Z-counter from reset counter. | Annex D-1, Annex D-2, Annex G, BIR blueprint. | New capability. Needs coordination with Central PMS for finalized sales window. |
| BIR Sales Summary Service | Produces Annex E-1 BIR Sales Summary and reconciles to Sales Invoice, Z-read, reset counter, Z-counter, Grand Total Amount, and fiscal lines. | Annex E-1, Annex F via Hikvision gap. | New capability. |
| Entitlement Fiscal Treatment Service | Handles fiscal treatment categories for Senior Citizen, PWD, NAAC, Solo Parent, and Diplomat VAT Privilege / VAT Exemption. | Annex E-2 to E-5; RMO No. 10-2019 per review instruction. | Senior/PWD can reference v1.2 statutory validation; NAAC/Solo Parent are future-supported; Diplomat VAT Privilege / VAT Exemption is active. |
| Electronic Journal Service | Maintains complete sequential ledger/replica of Sales Invoices, adjustment documents, X/Z reports, and exports. | Annex G, BIR blueprint, Hikvision checklist. | New fiscal ledger; may reuse audit patterns but should remain distinct from general audit. |
| POSLog Export Service | Produces ARTS POSLog with BIR, vehicle, entitlement, tender, tax, and fiscal line extensions and reconciles to EJ/documents. | BIR blueprint, Hikvision checklist. | New export capability. |
| Fiscal Audit and Reprint Controls | Logs fiscal administrative actions, reprints, exports, void/refund/cancel, Z-close, reset, restore, recovery, and configuration changes. | Annex G audit trail; Hikvision gap/checklist; DR decision. | Can reuse v1.2 audit infrastructure but needs fiscal-specific events and retention. |
| Tamper-Evident Fiscal State Store | Preserves append-only fiscal state and externally anchored continuity for Grand Total Amount, reset counter, Z-counter, latest Sales Invoice number, latest EJ hash, and last fiscal event timestamp. | BIR anti-tamper expectations; review decision. | New System Design area. |
| Fiscal Storage and Retention | Stores immutable payloads, rendered documents, hashes, export files, evidence references, and retention policies. | BIR blueprint retention and backup rules. | New or extended storage policy. |

## Channel Impact

| Channel / terminal | Required impact | Key open issue |
| --- | --- | --- |
| WebPay | Must receive/display/download Sales Invoice after Central PMS payment finality and POS Server issuance. Must not bypass Central PMS. | WebPay has no physical terminal serial/printer. Need fiscal terminal identity and print/download policy. |
| AutoPay Machine | Must become one terminal/channel under the Site POS Server. APM printer may print POS Server-issued Sales Invoice or locally render a POS Server-issued payload. | MIN/PTU/serial mapping and APM print control remain open. |
| Cashier POS | Must support cashier login/session, cash/tender capture, reprint/void/refund controls, X-read and cashier accountability under Site POS Server. | Need fiscal RBAC, cash drawer/session rules, and terminal identity. |
| EC Device / Continuity Terminal | Must be modeled as a terminal/channel if it accepts, confirms, or supports payment/fiscal continuity operations. | Need definition of fiscal identity, offline policy, and continuity constraints. |
| Operator-assisted payment | Must flow through Central PMS finality and Site POS Server issuance, with operator identity included in fiscal audit where applicable. | Need distinguish assistance from cashier fiscal responsibility. |
| Future channels | Must register as terminals/channels under POS Server rather than creating separate fiscal authorities. | Need extensible terminal/channel taxonomy. |

## Data And Contract Impact

| Area | Needed by POS Server | Current v1.2 source / gap |
| --- | --- | --- |
| Site routing | `site_id`, `site_group_id`, taxpayer/branch fiscal profile, POS Server ID. | v1.2 parking sessions and site tables already carry site scope. POS Server mapping is missing. |
| Fiscal identity | POS Server and terminal/channel MIN, PTU, serial number, terminal number, supplier accreditation fields, software version. | Required by Annex G/BIR blueprint. Assignment remains open. |
| Payment context | Payment attempt, confirmation, provider transaction reference, rail/tender type, confirmed amount, timestamps. | v1.2 payment attempts/confirmations/provider outcomes exist. Fiscal tender normalization may need more fields. |
| Fiscal line basis | VATable sales, VAT-exempt sales, zero-rated sales, non-VAT sales, statutory discounts, VAT privileges/exemptions, coupons, penalties, lost ticket fees, overstay charges, service charges, other fiscal adjustments. | v1.2 tariff snapshots cover gross/discount/payable but not full fiscal tax line detail. |
| Customer/buyer fields | Buyer name/address/TIN/business style when required, including Diplomat VAT Privilege / VAT Exemption evidence/identity fields if confirmed. | Annex G requires buyer details for VAT OR/SI; Diplomat implementation details remain open. |
| Entitlement fields | Senior, PWD, NAAC, Solo Parent, Diplomat VAT Privilege / VAT Exemption categories and evidence references. | v1.2 covers Senior/PWD operationally; NAAC/Solo Parent future; Diplomat active but implementation details open. |
| Fiscal document state | Issued, reprinted, voided/cancelled/refunded/returned, linked original/adjustment docs, exception/retry state. | Missing in v1.2. |
| Counter state | Sales Invoice sequences, reset counter, Z-counter, Grand Total Amount, first/last numbers, externally anchored last state. | Missing in v1.2. |
| Fiscal reports | X-read, Z-read, BIR Sales Summary, Annex E sales books, EJ, POSLog, Diplomat treatment reports if required. | Missing in v1.2. |

## Reset, Z-Counter, And DR Impact

| Topic | Required behavior |
| --- | --- |
| Reset counter | Starts from zero and increments by one only when a fiscal reset event occurs. |
| Reset audit reference | Preserve previous Grand Total Amount, previous reset counter, reset timestamp, reset reason, approving user, and recovery/reference notes. |
| Z-counter | Separate from reset counter and advances per Z-reading / fiscal day close. |
| Restore/failover continuity | Never resume from a lower fiscal counter, lower Grand Total Amount, lower Z-counter, or earlier Sales Invoice sequence than the last externally anchored fiscal state. |
| Continuity proof | Preserve last Grand Total Amount, reset counter, Z-counter, latest Sales Invoice number, latest EJ hash, and last fiscal event timestamp as audit reference. |
| Unproven continuity | Require supervised recovery and recovery audit record before fiscal issuance resumes. |

## Risks

| Risk | Why it matters | Mitigation planning note |
| --- | --- | --- |
| Treating APM as the only fiscal scope | Would exclude WebPay/cashier/operator-assisted channels from BIR-compliant fiscal issuance. | Keep Site POS Server as source BRD architecture. |
| Letting Central PMS issue Sales Invoices directly | Blurs payment finality and fiscal authority boundaries. | POS Server owns Sales Invoice issuance; Central PMS records fiscal references. |
| Issuing ExitAuthorization before fiscal issuance | Creates paid-but-not-fiscally-issued exits and weakens fiscal control. | Enforce payment finality, POS issuance, fiscal reference recording, then ExitAuthorization. |
| Document sequence gaps | BIR sources require sequential/no-gap behavior and adjustment logging. | Design idempotent fiscal issuance and failed-reservation handling. |
| Offline terminal issuance | Could create duplicate numbers or unreconciled sales. | Define offline policy before implementation. |
| Incomplete tax/fiscal basis | Annex E and Sales Invoice outputs require detailed classifications. | Add explicit fiscal line classification model before implementation. |
| Treating Diplomat VAT Privilege / VAT Exemption as a discount | Could misstate VAT treatment and reporting. | Model as active VAT privilege/exemption entitlement under RMO No. 10-2019. |
| Privacy conflict in Annex E and Diplomat evidence | Reports/evidence may require personal or diplomatic identity data. | Legal/privacy review before storing or rendering details. |
| Server DR counter reset | Reset/Z/grand total/Sales Invoice sequence rollback could break fiscal integrity. | Define tamper-evident fiscal state store and recovery procedure. |

## Not In This Artifact

- No database schema changes.
- No source code changes.
- No final BRD.
- No DOCX output.
