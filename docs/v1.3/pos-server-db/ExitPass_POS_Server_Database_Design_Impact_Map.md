# ExitPass POS Server Database Design Impact Map

## 1. Purpose

This impact map identifies likely database design impacts for POS Server Database Design v1.0. It is planning-only and uses provisional topic names.

## 2. Impact Summary

| Area | Database impact | Upstream source | Downstream dependency |
| --- | --- | --- | --- |
| Fiscal documents | Sales Invoice and fiscal adjustment records, status, numbering, original linkage, digital SI URL reference, audit reference. | BRD, System Design, API Contract | BIR/accounting numbering decisions, API DTOs |
| Fiscal lines | Explicit ordered fiscal lines for tax, discounts, VAT privileges, fees, penalties, coupons, adjustments. | BRD, System Design, ARTS impact review | VAT/tax confirmation, DB design |
| Fiscal identity | Taxpayer, Site/branch/business unit, POS Server, channel/terminal, MIN/PTU/serial/software/supplier metadata. | BRD, System Design, BIR refs | BIR/accounting and supplier/applicant decisions |
| Channel/terminal registry | Registration, type, Site POS Server association, capabilities, ONLINE/OFFLINE health, active/degraded/continuity state. | System Design, API Contract | Operations, Security/RBAC, Vendor/supplier |
| Numbering/counters | SI sequence, adjustment sequence, reset counter, Z-counter, GTA, previous snapshots, EJ hash, last event timestamp. | BRD, System Design, API Contract | BIR/accounting, recovery design |
| Idempotency/retry | Idempotency key/scope, semantic hash, linked operation, replay result, conflict, timeout/completion unknown, retry status. | API Contract | API finalization, DB constraints/indexes later |
| Digital SI URL | URL/token/access reference, status, issue/expiry timestamps, repeated access audit, revocation/blocking. | BRD, System Design, API Contract | Security/Privacy Review |
| QR responsibility | Capability and presentation audit metadata only; no required QR image storage as fiscal authority. | Updated System Design/API Contract | Channel implementation |
| Reprints | Reprint requests/history for SI, X-read, Z-read, EJ, reason, actor, approval, timestamp, labels, audit. | Updated BRD/System Design/API Contract | BIR layout confirmation, RBAC |
| Adjustments | Void/refund/cancel/return document linkage, reason, actor, approval, refund/reversal context, reconciliation, audit. | BRD, System Design, API Contract | Finance/payment workflow confirmation |
| Reports | X-read, Z-read, BIR Sales Summary, Annex E, report request/status/scope/output references. | BRD, System Design, API Contract, BIR refs | X/Z scope and export layout confirmation |
| BIR Sales Summary | Required summary fields, counters, SI ranges, GTA, sales/tax/discount/void/return totals. | API Contract, BIR impact review | Accounting confirmation |
| EJ/POSLog/exports | EJ records, POSLog exports, JSON/POSLog schema version, ARTS profile reference, validation status/errors, package metadata. | System Design, API Contract, ARTS refs | Engineering Pack, BIR/accreditation |
| Audit | Fiscal and privileged action audit, export validation, channel/fiscal identity changes, ONLINE/OFFLINE changes, unauthorized actions. | BRD, System Design, API Contract | Retention/security review |
| Recovery/continuity | Latest/previous fiscal state, continuity checks, recovery request/approval, external anchor reference if used, block/resume status. | BRD, System Design, API Contract | Tamper-evidence mechanism, operations |
| Security/privacy | Actor identity, roles, approvals, evidence references, sensitive evidence separation, privileged export access audit. | BRD, System Design, API Contract | Security/Privacy Review |
| Central PMS integration | References to parking session, PaymentAttempt, PaymentConfirmation, fiscal issuance reference, exception/retry state. | v1.2 DDL, BRD, API Contract | Central PMS contract alignment |
| Engineering Pack | State-based object layout, drift checks, export validation jobs, sample data, operational validation. | User instruction, v1.2 DB baseline | Engineering Pack planning |
| Accreditation package | Sample outputs, schema validation evidence, audit trail report, supplier/applicant metadata, sample fiscal records. | BIR/ARTS impact review | BIR/accreditation package |

## 3. Authority Boundary Impacts

| Boundary | Database planning rule |
| --- | --- |
| Central PMS payment finality | POS Server DB should store payment finality references supplied by Central PMS, not determine finality. |
| Central PMS ExitAuthorization | POS Server DB should not store POS-owned ExitAuthorization authority state. Fiscal reference storage may support Central PMS workflow only. |
| Payment Orchestrator/WebPay | POS Server DB should not treat provider or WebPay outcome records as platform finality without Central PMS context. |
| Vendor PMS / HikCentral | Vendor acknowledgment/projection references are synchronization only, not POS fiscal authority or exit authority. |
| POS/fiscal events | Event/audit records are observability/integration artifacts, not payment or exit authority. |

## 4. Candidate Data Area Crosswalk

| Candidate data area | Related API family | Related DB planning risks |
| --- | --- | --- |
| Site POS Server fiscal boundary | Fiscal identity, channel registry, issuance | MIN/PTU assignment and supplier/applicant ambiguity. |
| Fiscal document records | Fiscal issuance, fiscal documents, adjustments | Numbering, idempotency, sequence gaps. |
| Fiscal lines/totals | Issuance, reports, exports | VAT/tax confirmation and ARTS mapping. |
| Numbering/counter state | Issuance, X/Z, reset/recovery | Rollback prevention, reserved/abandoned numbers. |
| Idempotency/retry | Issuance, exception/retry | Retention, semantic conflict, completion unknown. |
| Digital SI URL access | Digital SI | Token/access/expiry/auth model and privacy. |
| Reprint history | Reprints | Label/timestamp metadata and RBAC. |
| Report snapshots | X/Z, BIR/Annex E | Scope and layout uncertainty. |
| Export packages | EJ/POSLog/exports | Schema profile/versioning and validation errors. |
| Audit trail | Audit/events | Retention, tamper-evidence, privileged action review. |
| Recovery state | Reset/recovery | External anchoring and supervised recovery model. |

## 5. State-Based Versioning Impacts

Future database work should plan:

- Repository-owned object definitions for repeatable rebuilds.
- Per-object representation where practical.
- Validation scripts for fiscal tables, constraints, indexes, and routines once designed.
- Drift checks comparing expected state to actual database state.
- No promotion of local drift into baseline without explicit review.
- Clear split between conceptual design, physical DDL, seed/reference data, and validation scripts.

## 6. No Immediate Physical Change

This planning package does not require or create:

- SQL DDL.
- Atlas state/migration files.
- Patch scripts.
- Seed scripts.
- Code changes.
- Existing schema changes.
