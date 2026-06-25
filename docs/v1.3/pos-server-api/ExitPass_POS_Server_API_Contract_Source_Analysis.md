# ExitPass POS Server API Contract Source Analysis

Status: Initial API contract planning artifact only

This artifact identifies source-driven inputs for `ExitPass POS Server API Contract v1.0`. It does not define final endpoint paths, DTOs, database tables, schemas, status codes, or event payloads.

## 1. Approved BRD Requirements

| Source area | API contract input | Planning implication |
| --- | --- | --- |
| Platform-wide POS/Invoicing | APIs must support WebPay, APM, Cashier POS, EC Device / Continuity Terminal, operator-assisted payment, and future channels through the resolved Site POS Server. | Channel-specific contracts must preserve a common Site POS Server fiscal authority pattern. |
| Site-level POS Server model | The resolved Site determines which POS Server issues the Sales Invoice. | API calls must carry or reference authoritative Site/session/payment context from Central PMS. |
| Authority split | Central PMS owns payment finality and ExitAuthorization. POS Server owns fiscal issuance. | POS Server APIs must not expose any operation that issues ExitAuthorization or declares payment finality. |
| Fiscal issuance before ExitAuthorization | Central PMS requests fiscal issuance after verified payment finality and records fiscal reference before issuing ExitAuthorization. | Fiscal issuance API must return fiscal document identity/status and failure states clearly enough for Central PMS to withhold authorization when needed. |
| Sales Invoice as primary output | Successful parking payment fiscal output is Sales Invoice. | Fiscal issuance family must center on Sales Invoice issuance and status. |
| Printed/digital SI consistency | Printed and digital SI must represent the same fiscal document and fiscal facts. | Document lookup, print, digital URL, re-access, and reprint contracts must reference the same issued fiscal document. |
| Digital SI URL | POS Server returns a digital SI URL after SI issuance where digital delivery is enabled. | Issuance and document/presentation families need a digital SI URL return and access policy hooks. |
| QR presentation | QR presentation is a channel/terminal capability, not APM-only. | API must provide presentation metadata without making the channel/terminal fiscal issuer. |
| Offline fiscal issuance | Offline fiscal issuance is restricted/disabled by default until approved. | Contract must not create an implicit offline issuance path unless BIR/accounting approves a compliant model. |
| Fiscal reports and exports | X-read, Z-read, BIR Sales Summary, Annex E, EJ, POSLog, and exports are required fiscal outputs. | Report/export families need request/status/export semantics while final formats remain open. |
| Reprints and adjustments | Reprints and void/refund/cancel/return actions must be controlled, auditable, and linked. | Reprint and adjustment API families need authorization, original document linkage, reason, status, and audit treatment. |

## 2. POS Server System Design Sections

| System Design section | API contract input |
| --- | --- |
| Section 7 Authority Model | Contract must preserve Central PMS and POS Server boundaries. |
| Section 8 Component Architecture | Logical services imply route families: issuance, documents, presentation, registry, identity, reports, exports, adjustments, reset/recovery, and audit/status. |
| Section 9 Channel and Terminal Registration | Registry API family is needed for channel/terminal identity, capability, status, and audit. |
| Section 10 Fiscal Identity Model | Fiscal identity configuration API family is needed, but final MIN/PTU/serial assignment remains open. |
| Section 11 Sales Invoice Lifecycle | Issuance API requires idempotency, status, retry, fiscal identity/status return, digital SI URL return, and Central PMS fiscal reference recording. |
| Section 13 Digital SI URL and QR Code Model | Presentation API family must support digital SI URL and QR presentation metadata while access policy remains open. |
| Section 14 Fiscal Document Numbering | Contract must account for sequence, gap, reserved number, failed issuance, abandoned issuance, and duplicate request behavior without finalizing BIR treatment yet. |
| Section 17 X-read and Z-read | X/Z API family must support approved fiscal scopes once confirmed. |
| Sections 19-22 Reports and exports | API must support BIR Sales Summary, Annex E, EJ, POSLog, and other fiscal exports with final formats open. |
| Sections 23-24 Reprints and adjustments | API must support controlled reprint and fiscal adjustment workflows. |
| Sections 28-30 Integrity, recovery, retry | Contract must expose reset/recovery/status semantics without weakening fiscal continuity. |
| Sections 38-40 Eventing, database, API impact | API contract must define contract boundaries later without overloading this planning artifact. |
| Section 44 Open Questions | API planning must carry forward unresolved BIR/accounting/security/privacy questions. |

## 3. v1.2 API Contract Constraints

The v1.2 API Contract Pack is a supporting baseline for platform authority and integration style. This planning artifact does not rewrite the v1.2 API Contract.

Source-driven constraints to preserve:

- Central PMS remains the platform authority for parking session, site resolution, payment authority chain, and ExitAuthorization.
- Payment Orchestrator verifies provider outcomes but does not become platform payment finality authority.
- Gate/exit integrations consume Central PMS authorization and must not be directly authorized by POS Server.
- API contracts should preserve idempotency, auditability, and clear actor/system responsibility where workflows cross system boundaries.
- POS Server API Contract v1.0 should be a companion contract, not a replacement for the entire v1.2 API Contract Pack.

## 4. v1.2 Authority Model

| Authority concept | API planning rule |
| --- | --- |
| Payment finality | Only Central PMS-facing flows may record platform finality; POS Server APIs consume finality context but do not declare it. |
| PaymentAttempt / PaymentConfirmation | POS Server API may reference these concepts through Central PMS context but must not own their lifecycle. |
| ExitAuthorization | POS Server API must not expose authorization creation, update, or release operations. |
| Vendor PMS / HikCentral | Any acknowledgment is synchronization/status only, not fiscal or exit authority. |
| Operator Console controls | API auth/RBAC must support high-risk fiscal actions such as reprint, adjustment, export, reset, and recovery approval. |

## 5. BIR/POS Compliance Requirements

| Compliance area | API planning implication |
| --- | --- |
| Sales Invoice identity/header/footer | Fiscal identity configuration and document output contracts must support BIR-required metadata once assignment is confirmed. |
| MIN/PTU/serial/software/supplier metadata | Contract must leave assignment open and avoid hard-coding server vs terminal responsibility. |
| X-read / Z-read | Request/status/export semantics are required, with fiscal scope open. |
| Reset counter / Z-counter / GTA | Status/report contracts must surface or reference counter state according to approved design. |
| BIR Sales Summary / Annex E | Report/export API families must support required reports and future statutory categories. |
| EJ / POSLog | Export API family must support confirmed EJ/POSLog output formats later. |
| Voids/refunds/cancels/returns | Adjustment API must preserve original document linkage and audit controls. |
| Accreditation sample set | API planning must support generating or retrieving representative samples, but exact sample set remains open. |

## 6. Digital SI URL / QR Presentation Requirements

API contract planning must support:

- POS Server-returned digital SI URL after successful SI issuance where enabled.
- Public/customer access model that remains open for security/privacy/API design.
- URL expiry policy that remains open.
- Authentication/access model that remains open.
- Digital SI access audit treatment that remains open.
- Presentation metadata for channels/terminals.
- QR code display/print support for APM, Cashier POS, EC Device / Continuity Terminal, operator-assisted terminals, and future channels where supported.
- QR rendering responsibility as an API/implementation open question.
- Guarantee that digital SI and printed SI correspond to the same fiscal document and fiscal facts.

## 7. Open Questions

Open questions carried into API planning include:

- Final route family naming.
- Request/response DTO boundaries.
- Idempotency key scope.
- Duplicate issuance behavior.
- Sequence gaps, reserved numbers, failed issuance, and abandoned issuance.
- Digital SI URL token/access model, expiry, authentication, and audit treatment.
- QR presentation payload and rendering responsibility.
- WebPay fiscal identity.
- APM printing model.
- Channel/terminal registry fields.
- Fiscal identity fields and ownership.
- X/Z scope.
- Report/export formats.
- Fiscal adjustment workflow sequencing.
- Refund/reversal relationship with Central PMS/provider.
- Recovery continuity API.
- Offline fiscal issuance restriction.
- Audit/event publication contracts.
- Error/status model.
- Security/RBAC model.

## 8. Downstream API Contract Implications

The future POS Server API Contract v1.0 should define:

- API ownership and consumers.
- Authentication and authorization model.
- Common request metadata and correlation rules.
- Idempotency and retry semantics.
- Canonical status and error model.
- Route families without violating authority boundaries.
- Document and report status semantics.
- Presentation URL and QR metadata semantics.
- Audit/event publication impact.
- Contract responsibilities for Central PMS, WebPay, APM, Cashier POS, EC/continuity, operator-assisted flows, and future channels.

It should not decide BIR/accounting, security/privacy, or database implementation questions without the appropriate confirmation.
