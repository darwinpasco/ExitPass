# ExitPass POS Server Database Design Source Analysis

## 1. Purpose

This planning artifact identifies source-driven database design inputs for the future `ExitPass_POS_Server_Database_Design_v1.0.md`.

This is planning only. It does not define final physical tables, columns, indexes, constraints, SQL, Atlas migrations, or database patches.

## 2. Sources Reviewed

### Approved Baselines

| Source | Database relevance |
| --- | --- |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Business requirements for platform-wide POS/Invoicing, Site-level POS Server fiscal authority, Sales Invoice issuance, fiscal reports, entitlement/tax treatment, digital SI, reprints, and audit. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | Technical design for logical POS Server components, fiscal lifecycle, counters, recovery, reports, EJ, POSLog, exports, QR responsibility, and operational status. |
| `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md` | Approved API contract baseline for fiscal issuance, documents, digital SI URL, registry, identity, reprints, adjustments, reports, exports, recovery, exception/retry, status, and error semantics. |

### Review and Impact Sources

| Source | Database relevance |
| --- | --- |
| `docs/v1.3/pos-server-api/ExitPass_BIR_ARTS_Source_Impact_Review.md` | Carries BIR/ARTS impacts into database planning, including ARTS POSLog 6.x concepts, BIR/local extensions, structured export validation, reprint metadata, and ONLINE/OFFLINE status. |
| `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Technical_Review.md` | API review findings and boundaries for idempotency, retries, authority, QR, reports, and open questions. |
| `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Approval_Readiness_Review.md` | Confirms approval-readiness of the pre-cleanup API contract baseline. |
| `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Post_Cleanup_Approval_Readiness_Review.md` | Confirms BIR/ARTS cleanup readiness and identifies no P0/P1 blockers. |

### v1.2 Repository Database References Inspected

| Source | Observed relevance |
| --- | --- |
| `docs/ExitPass-v1.2-database-rebuild-baseline.md` | Establishes `ExitPass_Full_Database_Creation_DDL_v1.2.sql` as authoritative clean rebuild DDL baseline; emphasizes controlled updates and validation. |
| `ExitPass_Full_Database_Creation_DDL_v1.2.sql` | Existing v1.2 full DDL reference for Central PMS, sessions, payments, ExitAuthorization, coupons, discounts, evidence references, audit attribution, and validation patterns. |
| `infra/db/seed/ExitPass_Reference_Data_v1.2.sql` | Existing deterministic seed baseline for clean rebuilds. |
| `infra/db/patches/*.sql` and validation scripts | Existing patch/validation style for controlled DB changes, though future v1.3 POS Server DB work should align with state-based repository source of truth. |
| `docs/ExitPass-v1.2-database-rebuild-baseline.md` and `infra/db/scripts/Reset-ExitPassV12Database.ps1` | Clean rebuild and drift-check context for future validation planning. |
| `D:\Docs\ExitPass\v1.2\ExitPass_DDL_v1.2_Data_Dictionary.xlsx` and `ExitPass_DDL_v1.2_Constraint_Matrix_and_Index_Inventory.xlsx` | Supporting v1.2 data dictionary/index inventory references. They were identified as relevant but not modified. |

### Local POS / BIR / ARTS References

| Source | Database relevance |
| --- | --- |
| `D:\Docs\ExitPass\POS\FINAL GAP ANALYSIS - Hikvision AutoPay Machine BIR Accreditation.docx` | BIR examiner-driven requirements for SI terminology, simplified print, complete digital records, reprints, reprint labels, BIR Sales Summary minimum contents, and required report set. |
| `D:\Docs\ExitPass\POS\BIR POS Accreditation Requirements.docx` | JSON/POSLog schema concepts, fiscal identity, EJ/POSLog/export, tender/tax/discount/totals, sequence, and accreditation package inputs. |
| `D:\Docs\ExitPass\POS\BIR Recommended Formats.pptx` | Audit trail/report presentation support; mostly image-oriented after text extraction. |
| `D:\Docs\ExitPass\POS\ARTS POSLog` | ARTS POSLog v6.0 readme, technical spec PDFs, and XSDs supporting POSLog export mapping concepts such as Business Unit, Workstation, Business Day Date, transaction sequence, line items, tender, tax, totals, and extensions. |
| RMO No. 24-2023 and Annex D/E/F/G references | BIR fiscal document/report/control requirements for SI, X-read, Z-read, Annex E, audit trail, reprints, footer/header identity, and counters. |

## 3. BIR / ARTS Source Relevance

ARTS POSLog v6.0 is a structured export/schema interoperability reference only. It must not replace Philippine BIR terminology or required fiscal outputs.

Database planning must preserve:

- Sales Invoice / SI / Sales Invoice Number terminology.
- Site POS Server as fiscal authority.
- BIR-required outputs: Sales Invoice, X-read, Z-read, EJ, POSLog, BIR Sales Summary, and Annex E reports.
- BIR fiscal identity metadata, including taxpayer, branch/site, MIN, PTU, serial, software, supplier/accreditation metadata where confirmed.
- Reprint metadata for `REPRINT` and `DATE / TIME REPRINTED`.
- BIR Sales Summary minimum content.
- Local/BIR extension or mapping support for ARTS-aligned POSLog exports.

## 4. State-Based Database Versioning Context

Future POS Server database work must preserve the repository as source of truth:

- Repository database artifacts must support repeatable rebuilds and drift checks.
- Database objects should be represented per object where practical, not only as one monolithic schema file.
- Database changes must align the actual database and Git repository.
- Local database drift must not become the baseline without explicit review and repository update.
- Atlas/state-based comparison may be used later, but this planning task does not create Atlas artifacts, SQL, or migrations.

## 5. Database Design Themes

| Theme | Planning implication |
| --- | --- |
| Authority separation | POS DB design stores fiscal records and references to Central PMS payment/session authority without owning payment finality or ExitAuthorization. |
| Fiscal boundary | Site POS Server fiscal boundary must map to Site / branch / business unit context and fiscal identity. |
| Canonical fiscal records | SI, EJ, POSLog, reports, exports, and audit should reconcile from canonical fiscal facts. |
| Explicit fiscal lines | Tax, tender, discount, VAT privilege, fee, adjustment, and line ordering need explicit support rather than hiding inside tariff snapshots. |
| Counter integrity | SI sequence, adjustment sequence, reset counter, Z-counter, GTA, EJ hash, and fiscal event timestamp require continuity planning. |
| Idempotency | Side-effecting operations need replay-safe storage planning without defining exact indexes yet. |
| Digital SI URL | URL reference, access state, audit, and privacy controls need storage planning; QR images are channel-side presentation behavior. |
| Reports and exports | Report requests, output references, schema versions, validation statuses, and export packages need planning. |
| Audit and recovery | Fiscal audit trail and tamper-evident continuity are core database concerns. |
| Security/privacy | Actor identity, approvals, sensitive evidence references, and digital access audit need separation and retention planning. |

## 6. Candidate Bounded Data Areas

The following are provisional planning areas, not final schemas:

| Bounded data area | Candidate responsibilities |
| --- | --- |
| Site POS Server fiscal boundary | Site POS Server identity, Site/branch/business unit mapping, taxpayer information, fiscal authority boundary, POS Server fiscal identity. |
| Channel and terminal registry | Child channel/terminal registration, type, Site POS Server association, capabilities, ONLINE/OFFLINE or health state, lifecycle state, fiscal identity references. |
| Fiscal documents | Sales Invoice and adjustment fiscal document records, SI number, status, issued timestamp, payment finality reference, parking session reference, digital SI URL reference, audit reference. |
| Fiscal lines | Ordered fiscal lines supporting VATable, VAT-exempt, zero-rated, non-VAT, discounts, VAT privileges, coupons, fees, penalties, service charges, and adjustments. |
| Tender/tax/discount/totals | Tender and provider references, tax/deduction totals, GTA contribution, BIR Sales Summary reconciliation support. |
| Numbering and counters | SI sequence, adjustment sequence, reset counter, Z-counter, GTA accumulator, previous snapshots, EJ hash, last fiscal event timestamp. |
| Idempotency and retry | Idempotency key/scope, request hash/semantic identity, linked operation, replay result, conflict, retry, completion unknown, retention. |
| Digital SI URL | URL/access reference, lifecycle status, issue/expiry timestamps, access audit, repeated access where required. |
| Reprints | Reprint request/history for SI, X-read, Z-read, EJ, reason, actor/service, approval, timestamp, labels, audit. |
| Fiscal adjustments | Void/refund/cancel/return fiscal document linkage, reason, actor/service, approval, payment refund/reversal reference, reconciliation, audit. |
| Reports | X-read, Z-read, BIR Sales Summary, Annex E, report request/status/scope/output references and audit. |
| EJ/POSLog/exports | EJ records, POSLog export records, JSON/POSLog schema version, ARTS profile reference, validation status/errors, package metadata, retention. |
| Audit trail | Fiscal issuance, digital SI access, reprints, adjustments, reports, exports, validation, recovery, identity/config changes, unauthorized actions, ONLINE/OFFLINE changes. |
| Recovery continuity | Latest and previous fiscal state, continuity checks, supervised recovery, external anchor reference if used, resume/block state. |
| Security/privacy | Actor identity references, role/permission references, approval records, evidence references, sensitive access audit. |

## 7. Downstream Dependencies

| Dependency | Reason |
| --- | --- |
| BIR/accounting confirmation | Fiscal identity assignment, numbering, X/Z scope, VAT/tax treatment, export/report layouts, offline issuance, Diplomat VAT treatment. |
| Security/Privacy Review | Digital SI URL access, token/auth/expiry model, evidence references, privileged export access, audit retention. |
| POS Server API Contract | Final endpoint/DTO semantics and status/error finalization will constrain persistence needs. |
| Engineering Pack | Validation jobs, export generation, state-based drift checks, packaging, operational jobs, runbooks. |
| BIR/accreditation package | Sample outputs, POSLog/EJ exports, audit trail reports, schema validation evidence, supplier/applicant package. |
| Operations | ONLINE/OFFLINE lifecycle, recovery playbooks, retention/archive operations, support workflows. |
| Vendor/supplier | APM printing model, terminal identity, supplier/applicant responsibility, hardware serial treatment. |

## 8. Explicitly Out of Scope

This planning task does not:

- Define final physical tables, columns, constraints, indexes, enums, or triggers.
- Create SQL migration files or Atlas migration/state files.
- Modify repository DDL, seed data, database schema, or source code.
- Modify approved BRD, approved System Design, or approved API Contract.
- Finalize endpoint names, DTOs, event payloads, RBAC matrix, ARTS POSLog profile, JSON schema versioning, or accreditation package contents.
- Approve offline fiscal issuance.
