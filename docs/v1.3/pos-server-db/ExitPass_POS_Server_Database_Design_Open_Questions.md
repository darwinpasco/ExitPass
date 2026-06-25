# ExitPass POS Server Database Design Open Questions

## 1. Purpose

This file carries unresolved database-impact questions into the future POS Server Database Design v1.0 work.

The approved BRD, System Design, and API Contract are not reopened here. Questions below are downstream design questions unless explicitly marked otherwise.

## 2. BIR / Accounting Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-BIR-001 | How are MIN, PTU, serial number, terminal number, software version, and supplier accreditation metadata assigned between Site POS Server and terminals/channels? | Fiscal identity structure, uniqueness, lifecycle, and effective dating. | Yes |
| DB-OQ-BIR-002 | What fiscal terminal identity should WebPay use without physical printer or hardware serial? | Channel registry and fiscal identity reference design. | Yes |
| DB-OQ-BIR-003 | What is the exact Sales Invoice numbering pattern? | SI sequence state, uniqueness, display fields, reset-counter relationship. | Yes |
| DB-OQ-BIR-004 | What is the exact adjustment document numbering pattern? | Adjustment sequence state and original document linkage. | Yes |
| DB-OQ-BIR-005 | How should sequence gaps, reserved numbers, failed issuance, and abandoned issuance be treated? | Numbering state, idempotency, audit, exception, and recovery state. | Yes |
| DB-OQ-BIR-006 | What X-read and Z-read aggregation scope is approved? Site, terminal, cashier/session, or combined? | Report scope tables, counter boundaries, and report snapshot keys. | Yes |
| DB-OQ-BIR-007 | What exact VAT/tax treatment applies by Site, taxpayer, transaction type, entitlement, and fiscal line? | Fiscal line classification, totals, report fields, and reconciliation. | Yes |
| DB-OQ-BIR-008 | What exact Diplomat VAT Privilege / VAT Exemption treatment, evidence, wording, reporting, and retention are required? | Entitlement/tax privilege storage, evidence references, reporting, retention. | Yes |
| DB-OQ-BIR-009 | What exact report/export formats and layouts are mandatory? | Export metadata, output references, validation profile, retention. | Partially |
| DB-OQ-BIR-010 | Is offline fiscal issuance allowed under any model? | Sequence/counter/evidence/reconciliation/recovery storage. | Yes if allowed |

## 3. Security / Privacy Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-SEC-001 | What digital SI URL token/access/expiry/authentication model is approved? | URL reference, status, expiry, token reference, access audit, revocation. | Yes |
| DB-OQ-SEC-002 | What digital SI data minimization and sensitive-data rules apply? | SI view model references, public access audit, redaction strategy. | Yes |
| DB-OQ-SEC-003 | What evidence must be stored for Diplomat VAT Privilege and statutory entitlements, and where? | Evidence reference, retention, restricted access, purge/redaction. | Yes |
| DB-OQ-SEC-004 | What final fiscal RBAC matrix is required? | Actor, role, approval, and privileged action references. | Partially |
| DB-OQ-SEC-005 | What audit retention and tamper-evidence requirements apply to privileged exports and digital SI access? | Audit retention, append-only strategy, archival. | Yes |

## 4. API Contract Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-API-001 | What final endpoint paths and DTOs will be approved? | Persistence boundaries and payload-to-record mapping. | Partially |
| DB-OQ-API-002 | What final status and error codes will be used? | Status fields or lookup/reference structures. | Partially |
| DB-OQ-API-003 | What idempotency key scope and semantic request identity are approved? | Idempotency storage keys, conflict detection, retention. | Yes |
| DB-OQ-API-004 | How should completion-unknown and retry status lookup behave? | Exception/retry status and replay result storage. | Yes |
| DB-OQ-API-005 | What event/outbox publication contracts are needed? | Event/outbox references and audit/event replay planning. | Partially |

## 5. Engineering Pack Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-ENG-001 | What state-based tooling pattern will be used for v1.3 POS DB objects? | Repository object layout, drift checks, rebuild scripts. | Yes |
| DB-OQ-ENG-002 | How will JSON/POSLog schema validation jobs run? | Export validation status/errors and operational job metadata. | Partially |
| DB-OQ-ENG-003 | How will export packages be generated, stored, and retained? | Output references, package metadata, storage pointers. | Partially |
| DB-OQ-ENG-004 | What test fixtures and certification samples must be generated from database state? | Seed/reference data and deterministic sample output planning. | Partially |

## 6. Database Design Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-DB-001 | Should POS Server use a separate schema namespace or multiple schemas by bounded area? | Physical object organization. | Yes |
| DB-OQ-DB-002 | What append-only/tamper-evident pattern is approved for fiscal records and audit? | Audit and fiscal state model. | Yes |
| DB-OQ-DB-003 | How should latest fiscal state and historical fiscal state snapshots be represented? | Recovery continuity, counters, external anchoring. | Yes |
| DB-OQ-DB-004 | How should canonical fiscal lines relate to current tariff snapshots and entitlement validation records? | Integration with existing v1.2 data structures. | Yes |
| DB-OQ-DB-005 | How should retention/archive boundaries be represented? | Archival, partitioning, purge/redaction. | Partially |

## 7. BIR / Accreditation Package Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-ACC-001 | What exact accreditation sample set is required? | Sample data and export package references. | Partially |
| DB-OQ-ACC-002 | What ARTS POSLog profile and local/BIR extension mapping must be demonstrated? | POSLog profile metadata and mapping references. | Yes |
| DB-OQ-ACC-003 | What audit trail report layout is expected by examiners? | Audit query/output references and report snapshots. | Partially |

## 8. Operations Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-OPS-001 | What ONLINE/OFFLINE status lifecycle and health source are required? | Channel/POS Server status history and audit. | Partially |
| DB-OQ-OPS-002 | What recovery procedure is approved after restore/failover/counter continuity failure? | Recovery request, approval, block/resume state. | Yes |
| DB-OQ-OPS-003 | What operational runbooks require database-visible status? | Support dashboards, alerts, and status references. | Partially |

## 9. Vendor / Supplier Questions

| ID | Question | Database impact | Blocks final DB design? |
| --- | --- | --- | --- |
| DB-OQ-VEN-001 | Who is the software supplier/applicant and who is the POS user/PTU applicant? | Accreditation metadata ownership and footer identity. | Yes |
| DB-OQ-VEN-002 | Can APM print POS Server-issued SI payload, or does another approved print model apply? | Terminal capability, print audit, fiscal identity, and output reference design. | Partially |
| DB-OQ-VEN-003 | What terminal hardware serial/source identifiers are available from each device/channel? | Terminal registry and fiscal identity mapping. | Partially |
