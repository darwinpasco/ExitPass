# ExitPass POS Server Database Design Decision Log

## 1. Purpose

This planning log separates decisions already established by approved documents from database design decisions that remain open. It does not create final schema decisions.

## 2. Approved Decisions Inherited From Baselines

| ID | Decision | Source | Database design implication |
| --- | --- | --- | --- |
| DB-D001 | ExitPass POS/Invoicing is platform-wide, not APM-only. | Approved BRD | Database design must support WebPay, APM, Cashier POS, EC/continuity, operator-assisted, and future channels. |
| DB-D002 | One Site-level POS Server is fiscal authority for one Site or parking operation boundary. | Approved BRD/System Design | Store fiscal records under Site POS Server and resolved Site boundary. |
| DB-D003 | Channels/terminals are children of the Site POS Server, not independent POS systems. | Approved BRD/System Design/API Contract | Registry storage should model channel/terminal association to Site POS Server. |
| DB-D004 | Central PMS owns parking session state, site resolution, payment finality, PaymentAttempt, PaymentConfirmation, and ExitAuthorization. | Approved BRD/System Design/API Contract | POS DB should store references to Central PMS authority records, not own those state machines. |
| DB-D005 | POS Server does not issue ExitAuthorization and does not declare platform payment finality. | Approved BRD/System Design/API Contract | No POS DB storage should imply exit authority or payment finality authority. |
| DB-D006 | Sales Invoice is the primary parking fiscal output. | Approved BRD | Fiscal document planning centers on SI records and SI number identity. |
| DB-D007 | Fiscal issuance must succeed before Central PMS issues ExitAuthorization, unless controlled exception policy allows release. | Approved BRD/System Design/API Contract | Fiscal issuance reference and exception/retry state must be storable and replay-safe. |
| DB-D008 | POS Server returns the digital SI URL. | Approved BRD/System Design/API Contract | Store URL/access references and status; do not require QR image storage as fiscal authority. |
| DB-D009 | Channel/terminal converts the returned URL into QR where supported. | Updated System Design/API Contract | Store capabilities and presentation/audit metadata only where needed. |
| DB-D010 | Offline fiscal issuance is disabled/restricted by default until BIR/accounting approves a compliant model. | Approved System Design/API Contract | Continuity storage must not imply offline fiscal issuance approval. |
| DB-D011 | Printed outputs are simplified while complete fiscal details remain digital. | Approved BRD/System Design | Canonical fiscal data must support richer digital records, EJ, POSLog, exports, and audit. |
| DB-D012 | Reprint coverage includes SI, X-read, Z-read, and EJ where applicable. | Updated BRD/System Design/API Contract | Reprint request/history storage must handle document/report/output types. |
| DB-D013 | Reprinted fiscal outputs show `REPRINT` and `DATE / TIME REPRINTED` where BIR requires them. | Updated BRD/System Design/API Contract | Reprint metadata must support timestamp/label rendering without mutating original facts. |
| DB-D014 | BIR Sales Summary / Annex E-1 minimum content semantics are included. | Approved API Contract | Report snapshot/storage planning must support required summary values. |
| DB-D015 | ARTS POSLog 6.x-aligned export posture is accepted where practical and accepted by BIR/accreditation. | Updated System Design/API Contract | POSLog export planning must support ARTS-aligned mapping and local/BIR extensions. |
| DB-D016 | ARTS POSLog does not replace Philippine BIR fiscal outputs. | Updated System Design/API Contract | Database design must preserve BIR terminology and output identities. |
| DB-D017 | JSON/POSLog exports should be schema-versioned and validation-capable. | Updated System Design/API Contract | Export request/status storage must include schema/profile/version and validation status/errors. |
| DB-D018 | ONLINE/OFFLINE is operational/status information only. | Updated System Design/API Contract | Health/reachability state must not authorize offline fiscal issuance. |
| DB-D019 | POS/fiscal events do not grant payment finality or ExitAuthorization. | Approved API Contract | Event/audit storage must not be modeled as authority state for payment or exit. |
| DB-D020 | State-based database versioning posture applies. | User instruction and repository context | Future DB design should support repository source of truth, repeatable rebuilds, and drift checks. |

## 3. Safe Planning Defaults

These are planning defaults, not final physical design decisions:

| ID | Planning default | Rationale |
| --- | --- | --- |
| DB-PD001 | Treat POS Server database as a fiscal subsystem with references to Central PMS authority records. | Preserves authority boundaries. |
| DB-PD002 | Use explicit fiscal document and fiscal line concepts in the future design. | Supports BIR reports, EJ, POSLog, audit, and reconciliation. |
| DB-PD003 | Treat all candidate data area names as provisional. | Avoids premature table naming and schema locking. |
| DB-PD004 | Plan append-only/tamper-evident patterns for fiscal state and audit. | Supports recovery continuity and audit requirements. |
| DB-PD005 | Plan schema/profile metadata for exports rather than hard-coding one final ARTS/JSON format now. | Exact schema profiles remain open. |
| DB-PD006 | Plan actor/service/approval references across high-risk fiscal actions. | Supports RBAC and audit while final RBAC matrix remains open. |

## 4. Non-Decisions

The following are not decided in this planning package:

- Final schema names.
- Final table names.
- Final columns, constraints, indexes, triggers, functions, and enums.
- SQL or Atlas migration/state files.
- Exact SI numbering pattern.
- Exact adjustment numbering pattern.
- Sequence-gap, reserved-number, failed-issuance, and abandoned-issuance treatment.
- Exact X-read/Z-read aggregation scope.
- Exact VAT/tax treatment.
- Exact Diplomat VAT implementation.
- Exact digital SI URL token/access/expiry/authentication model.
- Exact ARTS POSLog profile/schema mapping.
- Exact JSON schema versioning strategy.
- Final endpoint/DTO/event payload/RBAC matrix.
- Final accreditation sample package.

## 5. Pending Decision Areas for Final Database Design

| ID | Pending area | Primary owner/dependency | Why it matters |
| --- | --- | --- | --- |
| DB-P001 | Fiscal identity assignment across Site POS Server and terminals/channels. | BIR/accounting, compliance, vendor/supplier | Affects fiscal identity storage and uniqueness. |
| DB-P002 | Numbering, reserved numbers, sequence gaps, failed issuance, abandoned issuance. | BIR/accounting, API Contract, Database Design | Affects SI/adjustment sequence state and idempotency. |
| DB-P003 | X/Z aggregation scope. | BIR/accounting, operations | Affects report scope, counters, cashier/session/terminal relationships. |
| DB-P004 | Offline fiscal issuance, if ever approved. | BIR/accounting, operations, security | Affects sequence, counter, evidence, reconciliation, and recovery storage. |
| DB-P005 | Digital SI URL token/access/expiry/authentication. | Security/Privacy Review | Affects URL reference, access audit, retention, and data minimization. |
| DB-P006 | ARTS POSLog profile and JSON schema versioning. | BIR/accreditation, Engineering Pack | Affects export metadata and validation storage. |
| DB-P007 | Tamper-evident anchoring mechanism. | Database Design, Engineering Pack, Security/Privacy | Affects latest fiscal state, audit chain, and recovery controls. |
| DB-P008 | Evidence and sensitive data separation. | Security/Privacy, BIR/accounting | Affects Diplomat, statutory entitlement, and privileged access storage. |
