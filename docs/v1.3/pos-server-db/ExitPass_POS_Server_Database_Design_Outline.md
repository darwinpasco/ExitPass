# ExitPass POS Server Database Design v1.0 Outline

## 1. Purpose

This file proposes the structure for the future final document:

`docs/v1.3/pos-server-db/ExitPass_POS_Server_Database_Design_v1.0.md`

This is an outline only. It does not create the final database design.

## 2. Proposed Document Structure

1. Document Control
2. Purpose and Scope
3. Reference Baseline
4. Database Design Principles
5. Schema Boundary and Ownership
6. Conceptual Data Model
7. Fiscal Identity and Site POS Server Tables
8. Channel and Terminal Registry
9. Fiscal Documents
10. Fiscal Lines
11. Tender, Tax, Discount, and Totals
12. Numbering and Counter State
13. Idempotency and Retry
14. Digital SI URL and Access Audit
15. Reprints
16. Fiscal Adjustments
17. X-read and Z-read
18. BIR Sales Summary and Annex E
19. EJ, POSLog, JSON, and Exports
20. Audit Trail
21. Recovery and Tamper-Evident Continuity
22. Security/RBAC and Privacy Data Impacts
23. Retention and Archival
24. Central PMS Integration References
25. ARTS POSLog / BIR Extension Mapping
26. Open Questions
27. Risks and Mitigations
28. Future SQL / Atlas / State-Based Versioning Plan
29. Appendices

## 3. Section Planning Notes

### 1. Document Control

Track title, version, status, source baselines, output format, approval history, and explicit statement that the design is database design, not a migration script.

### 2. Purpose and Scope

Define POS Server database scope and exclusions. Preserve that Central PMS owns payment finality and ExitAuthorization.

### 3. Reference Baseline

List approved BRD, System Design, API Contract, BIR/ARTS impact review, v1.2 database baseline references, and local BIR/ARTS supporting references.

### 4. Database Design Principles

Cover repository source of truth, state-based versioning, per-object representation where practical, append-only fiscal posture, auditability, reconciliation, authority boundaries, privacy, retention, and no local drift promotion.

### 5. Schema Boundary and Ownership

Define POS Server database ownership boundaries and references to Central PMS, Payment Orchestrator, WebPay, Vendor PMS/HikCentral, channels, terminals, and operators.

### 6. Conceptual Data Model

Describe high-level entities and relationships using provisional names only.

### 7. Fiscal Identity and Site POS Server Tables

Plan Site POS Server identity, Site/branch/business unit, taxpayer, POS Server fiscal identity, terminal/channel fiscal identity references, and supplier/accreditation metadata.

### 8. Channel and Terminal Registry

Plan registration, type, Site POS Server association, fiscal identity reference, presentation capabilities, ONLINE/OFFLINE status, lifecycle state, and audit.

### 9. Fiscal Documents

Plan Sales Invoice and adjustment document records, identity, numbering, status, Central PMS references, digital SI URL reference, and original document linkage.

### 10. Fiscal Lines

Plan ordered fiscal line structure for VAT, tax, discounts, VAT privileges, coupons, fees, penalties, service charges, adjustments, and export line sequence needs.

### 11. Tender, Tax, Discount, and Totals

Plan tender, payment/provider references, tax/deduction totals, Grand Total Amount contribution, and BIR Sales Summary reconciliation support.

### 12. Numbering and Counter State

Plan SI sequence, adjustment sequence, reset counter, Z-counter, GTA accumulator, prior snapshots, EJ hash, last event timestamp, and continuity references.

### 13. Idempotency and Retry

Plan side-effecting operation idempotency, request semantic identity, replay result, conflict handling, completion unknown, retry, exception linkage, and retention.

### 14. Digital SI URL and Access Audit

Plan digital SI URL reference, token/access reference, lifecycle status, issue/expiry timestamps, access audit, repeated access, revocation, and privacy boundaries.

### 15. Reprints

Plan reprint requests/history for SI, X-read, Z-read, EJ, reason, actor/service, approval, timestamp, labels, status/history, and audit.

### 16. Fiscal Adjustments

Plan void/refund/cancel/return and related adjustment records, original document linkage, reason, approval, refund/reversal context, reconciliation, and audit.

### 17. X-read and Z-read

Plan report scope, report generation, Z-counter advancement, reset-counter non-advancement per Z-read, report snapshots, and output references.

### 18. BIR Sales Summary and Annex E

Plan BIR Sales Summary / Annex E-1 minimum contents, Annex E-2 to E-5 structures, Senior/PWD immediate support, NAAC/Solo Parent future-supported support, and Diplomat VAT reporting support.

### 19. EJ, POSLog, JSON, and Exports

Plan EJ records, POSLog exports, ARTS POSLog 6.x-aligned profile reference, JSON/POSLog schema versions, validation status/errors, export package metadata, output references, and retention.

### 20. Audit Trail

Plan fiscal audit records covering issuance, digital SI URL access, reprints, adjustments, reports, exports, validation, reset, recovery, identity/config changes, unauthorized actions, and ONLINE/OFFLINE status changes.

### 21. Recovery and Tamper-Evident Continuity

Plan latest/previous fiscal state, continuity check, supervised recovery, external anchoring if used, block/resume state, and recovery audit records.

### 22. Security/RBAC and Privacy Data Impacts

Plan actor identity, role/permission references, approval records, evidence references, sensitive evidence separation, privileged export audit, and privacy-sensitive data boundaries.

### 23. Retention and Archival

Plan retention categories for fiscal records, EJ, POSLog, exports, audit, digital SI access, evidence references, and archive/rebuild concerns.

### 24. Central PMS Integration References

Plan references to parking sessions, PaymentAttempt, PaymentConfirmation, payment finality context, fiscal issuance reference, and exceptions without owning Central PMS authority.

### 25. ARTS POSLog / BIR Extension Mapping

Plan mapping concepts for Business Unit/Site, Workstation/terminal, Business Day Date, transaction sequence, line item sequence, tender, tax, totals, and local/BIR extension fields.

### 26. Open Questions

Carry unresolved BIR/accounting, security/privacy, API, engineering, operations, vendor, and accreditation questions.

### 27. Risks and Mitigations

Cover authority leakage, fiscal sequence gaps, rollback, export mismatch, privacy exposure, over-specification, drift, and accreditation failure.

### 28. Future SQL / Atlas / State-Based Versioning Plan

Define future approach for physical database artifacts, object-level files, validation scripts, drift checks, rebuild scripts, seed/reference data, and Atlas/state-based comparison if used.

### 29. Appendices

Include glossary, acronyms, source traceability, candidate conceptual object list, and non-decisions.

## 4. Explicit Non-Goals for Final Database Design

The final database design should not:

- Move payment finality or ExitAuthorization into POS Server.
- Treat ARTS POSLog as replacement for BIR fiscal outputs.
- Approve offline fiscal issuance unless BIR/accounting confirms a compliant model.
- Hide fiscal tax treatment only inside tariff snapshots.
- Finalize API endpoint payloads or event schemas.
