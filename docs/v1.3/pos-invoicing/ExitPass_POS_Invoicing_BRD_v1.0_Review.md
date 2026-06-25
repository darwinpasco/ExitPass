# ExitPass POS/Invoicing BRD v1.0 Review

## 1. Review Summary

| Field | Value |
| --- | --- |
| Review target | `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` |
| Review baseline | POS/Invoicing source analysis, decision log, open questions, impact map, and decision recommendations |
| Review date | 2026-06-25 |
| Review type | Documentation review only |
| Review outcome | Revision recommended before BRD approval |

The BRD draft correctly carries the core approved decisions: platform-wide scope, Sales Invoice as the primary parking fiscal output, Site-level POS Server, child channel/terminal model, Central PMS authority for payment finality and ExitAuthorization, fiscal issuance before ExitAuthorization, reset counter vs Z-counter distinction, entitlement model, fiscal line classification, and major BIR fiscal report categories.

No P0 authority-model or core-decision violation was found. The draft is not yet approval-ready because several P1 gaps should be addressed before signoff, especially BIR Sales Invoice identity/footer requirements, open-question hygiene for numbering and X/Z scope, operator-assisted/future channel acceptance coverage, and stronger business requirement language around fiscal day/cashier/session scope.

## 2. Overall Finding

The BRD is directionally correct and stays mostly within BRD scope. It should move to a follow-up edit task rather than approval. The required edits are targeted: add missing open questions, strengthen several BIR/control requirements, add acceptance criteria for operator-assisted and future-channel coverage, and improve traceability for Sales Invoice identity/footer and fiscal numbering/counter requirements.

## 3. Must Fix Before BRD Approval

No P0 findings were found.

| ID | Severity | BRD section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| MF-001 | P1 | 16 Sales Invoice Requirements; 31 Open Questions | The BRD does not explicitly carry the still-open Sales Invoice and adjustment document numbering questions from the planning artifacts. | The decision log keeps exact Sales Invoice, adjustment document, and reset-counter placement/printing open. Omitting these from BRD open questions can make reviewers think numbering is already sufficiently resolved. | Add open questions for exact Sales Invoice/adjustment document numbering pattern and whether reset counter prints separately, appends to the fiscal number, or both. |
| MF-002 | P1 | 16 Sales Invoice Requirements | Sales Invoice identity/header/footer requirements are too generic. SI-003 says required business, taxpayer, Site, fiscal identity, transaction, amount, tax, and tender information as confirmed, but does not explicitly call out supplier accreditation, PTU/ATG, MIN, serial number, terminal number, and software version capability. | Annex G, Annex F-related gap notes, and the planning artifacts identify these fields as BIR fiscal document and footer requirements, even though assignment across server/terminal remains open. | Add a BRD-level requirement that POS Server shall support rendering required taxpayer, Site, machine/terminal, software, MIN/PTU/serial, and supplier accreditation metadata once the field-assignment decision is confirmed. |
| MF-003 | P1 | 17 X-read, Z-read, Reset Counter, and Grand Total Requirements; 31 Open Questions | The BRD leaves X-read/Z-read scope implicit but does not list the open question for terminal/cashier/site aggregation and Z-close scope. | Planning question POS-Q009 remains open and blocks POS Server System Design. Without this open question, the BRD may imply the scope is settled. | Add an open question for whether X/Z reporting is Site-level only, terminal-level, cashier/session-level, or a combined model. |
| MF-004 | P1 | 32 Acceptance Criteria | Acceptance criteria do not cover operator-assisted payment routing or future-channel registration under the Site POS Server. | Scope and channel model include operator-assisted and future channels. Acceptance criteria currently cover WebPay, APM, Cashier POS, and EC Device / Continuity Terminal only. | Add acceptance criteria that operator-assisted payment, if allowed, routes to the resolved Site POS Server and that future channels must register as child channels/terminals rather than independent POS systems. |
| MF-005 | P1 | 33 Requirements Traceability Matrix | Traceability does not include Sales Invoice identity/footer metadata, fiscal numbering open questions, reset audit snapshot details, BIR/accreditation sample package, or supplier/applicant responsibility. | The BRD includes or should include these requirements, but the matrix does not trace them to source decisions/open questions. | Add traceability rows for fiscal identity/footer metadata, numbering/reset-counter open questions, reset audit snapshot, accreditation sample package, and supplier/applicant responsibility. |

## 4. Should Fix Before BRD Approval

| ID | Severity | BRD section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| SF-001 | P1 | 14 Functional Requirements; 21 Fiscal Audit, EJ, POSLog, Export, and Retention Requirements | POSLog and EJ are covered, but the BRD does not explicitly require one canonical fiscal event/source record to reconcile Sales Invoice, EJ, POSLog, BIR Sales Summary, X/Z, and audit. | Decision recommendations flag EJ/POSLog reconciliation as a System Design blocker. BRD should set the business requirement without specifying the technical implementation. | Add a business requirement that all fiscal outputs shall be derived from reconciled canonical fiscal records so printed documents, EJ, POSLog, reports, and audit cannot diverge. |
| SF-002 | P1 | 18 BIR Sales Summary and Annex E Reporting Requirements | Annex E reporting is covered, but the BRD is weak on whether NAAC and Solo Parent report structures should be present now. It states the question is open, but does not state a minimum BRD expectation for model accommodation in report structures. | The planning decision says NAAC and Solo Parent are future-supported and must not be designed out. | Add a requirement that fiscal reporting structures shall be extensible to include NAAC and Solo Parent even if active workflows are deferred. |
| SF-003 | P1 | 20 Void, Refund, Cancel, Return, and Reprint Requirements | Adjustment document requirements are business-readable but may be too weak on original-document linkage, negative/reversal presentation, and non-input-tax warning where applicable. | Annex G and the planning source analysis identify adjustment document content controls. | Add BRD-level control language that adjustment documents shall reference the original fiscal document and present reversal/adjustment values and fiscal warnings according to BIR/accounting confirmation. |
| SF-004 | P1 | 21 Fiscal Audit, EJ, POSLog, Export, and Retention Requirements | Export requirements are intentionally open on exact formats, but the BRD does not explicitly preserve known candidate formats from the sources, especially TXT EJ replica, PDF/JSON, and ARTS POSLog. | Open questions include export formats. Listing known candidate formats helps avoid under-scoping exports without deciding final format. | Add non-final language that required export formats are expected to include BIR-confirmed EJ replica, printable/report exports, structured digital exports, and POSLog. Keep exact formats open. |
| SF-005 | P1 | 24 Security, RBAC, and Segregation of Duties | RBAC requirements are broad and do not mention segregation between cashier, supervisor, fiscal admin, auditor, and recovery roles. | Fiscal reset, restore, Z-close, reprint, export, and adjustment actions need stronger segregation of duties than ordinary operational access. | Add BRD-level role separation expectations without naming database roles or permissions. |
| SF-006 | P1 | 25 Data Privacy and Evidence Handling | Diplomat VAT Privilege / VAT Exemption evidence handling is included, but the BRD does not explicitly state that it may require BIR/DFA-issued documents such as VAT Certificate or VAT Identification Card pending confirmation. | The open question baseline calls out these evidence candidates. | Add them to OQ-006 or privacy text as candidate evidence requiring confirmation, without deciding which is mandatory. |
| SF-007 | P1 | 30 Risks and Mitigations | Risk table does not include the risk of wrong MIN/PTU/serial/software/supplier accreditation assignment. | This is one of the main remaining BIR/accounting open questions and can affect Sales Invoice validity. | Add a risk and mitigation for incorrect fiscal identity metadata assignment. |
| SF-008 | P1 | 31 Open Questions | The open questions omit supplier/applicant/accreditation responsibility and final accreditation sample set details are too broad. | Planning artifacts keep supplier/applicant identity and accreditation sample set open. | Add an open question for who is the software supplier/applicant and how that affects footer, manuals, source documentation, and accreditation package. Expand OQ-011 to list key sample categories or cross-reference the expected sample categories. |

## 5. Nice To Have / Editorial Improvements

| ID | Severity | BRD section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| ED-001 | Editorial | 1 Document Control | The branch field still says `docs/v1.3-pos-invoicing-brd`, while this review branch is `docs/v1.3-pos-invoicing-brd-review`. | This is harmless content-wise but may confuse document provenance. | Either remove branch from the BRD or update it during the next BRD edit. |
| ED-002 | Editorial | 35 Appendix B: Acronyms | EC is defined as "Emergency/Exception/Continuity" pending final naming. | This is acceptable, but the term may need final product naming before approval. | Mark EC acronym as pending final terminology or replace once product naming is confirmed. |
| ED-003 | P2 | 7 Stakeholders and Users | BIR/accreditation advisor is included, but POS user/PTU applicant and software supplier roles are not clearly separated. | Source materials distinguish software provider, POS user/PTU applicant, and hardware supplier. | Add separate stakeholder rows for POS user/PTU applicant and software supplier once confirmed. |
| ED-004 | P2 | 28 Assumptions | Assumptions include "BIR recommended the Site-level POS Server model." That is a decision, not only an assumption. | Decisions are already documented elsewhere. | Move or duplicate as a decided architecture premise rather than assumption-only wording. |

## 6. Authority Boundary Review

| Check | Result | Notes |
| --- | --- | --- |
| Central PMS owns site/session control state | Pass | Sections 2, 12, and 13 preserve Central PMS authority. |
| Central PMS owns payment finality | Pass | Sections 12, 13, 14, and 24 state payment finality remains under Central PMS. |
| Central PMS owns PaymentAttempt and PaymentConfirmation | Pass | Section 12 explicitly assigns both to Central PMS. |
| Central PMS owns ExitAuthorization | Pass | Sections 12, 13, 14, 22, 24, and AC-017 preserve this. |
| Payment Orchestrator does not declare platform payment finality | Pass | FR-006, SEC-004, AC-018 cover this. |
| WebPay does not declare platform payment finality | Pass | FR-006, CH-WP-002, SEC-004, AC-018 cover this. |
| POS Server does not issue ExitAuthorization | Pass | FR-005, Section 12, EXC-006, SEC-003, AC-017 cover this. |
| Gate/exit execution does not bypass Central PMS authorization | Pass | Section 12 and channel requirements state this. |
| Fiscal issuance before ExitAuthorization | Pass | Sections 13, 14, 22, and AC-001/AC-002 cover this. |

No authority-boundary violation was found.

## 7. BIR / POS Compliance Review

| Area | Result | Notes |
| --- | --- | --- |
| Sales Invoice primary output | Pass | Consistent across executive summary, scope, functional requirements, Sales Invoice requirements, and acceptance criteria. |
| Official Receipt as primary output | Pass | No incorrect remaining OR-as-primary language found. OR appears only in glossary/acronym context. |
| X-read and Z-read | Partial | Capability is present, but scope/aggregation remains under-specified and should be listed as open. |
| Reset counter and Z-counter | Pass | BRD clearly distinguishes reset counter from Z-counter. |
| BIR Sales Summary | Pass with improvement | First-class report requirement is present. Add stronger traceability and fiscal identity/footer coverage. |
| Annex E reports | Pass with improvement | Senior/PWD immediate and NAAC/Solo Parent future support are reflected. Add stronger report-structure extensibility language. |
| EJ and POSLog | Pass with improvement | Both are present. Add a canonical fiscal record/reconciliation business requirement. |
| Reprints | Pass | Reprint label/audit controls are present. Exact label placement remains open. |
| Void/refund/cancel/return controls | Partial | Controls are present, but original-document linkage and reversal presentation should be stronger. |
| Fiscal identity/footer metadata | Partial | Mentioned generically; should explicitly preserve required BIR metadata fields while keeping assignment open. |

## 8. Channel Coverage Review

| Channel | Result | Notes |
| --- | --- | --- |
| WebPay | Pass | Covered in scope, channel model, specific requirements, open questions, and acceptance criteria. |
| AutoPay Machine / APM | Pass | Covered in scope, channel model, APM requirements, open questions, and acceptance criteria. |
| Cashier POS | Pass | Covered in scope, channel model, Cashier POS requirements, and acceptance criteria. |
| EC Device / Continuity Terminal | Pass | Covered in scope, channel model, EC requirements, and acceptance criteria. |
| Operator-assisted payment if allowed | Partial | Covered in requirements, but missing dedicated acceptance criterion. |
| Future payment channels | Partial | Covered in model and NFR-005, but missing dedicated acceptance criterion and traceability row. |

## 9. Entitlement and VAT Privilege Review

| Category | Result | Notes |
| --- | --- | --- |
| Senior Citizen | Pass | Immediate operational workflow is stated in FR-023, ENT-002, AC-011. |
| PWD | Pass | Immediate operational workflow is stated in FR-023, ENT-003, AC-011. |
| NAAC | Pass with improvement | Future-supported status is clear. Add stronger report-structure extensibility language. |
| Solo Parent | Pass with improvement | Future-supported status is clear. Add stronger report-structure extensibility language. |
| Diplomat VAT Privilege / VAT Exemption | Pass with improvement | Active VAT privilege/exemption status and non-discount treatment are clear. Add candidate evidence examples in open question/privacy sections. |

No language was found that treats Diplomat VAT Privilege / VAT Exemption as future-only or as an ordinary commercial discount.

## 10. Open Questions Review

| Required open item | Result | Notes |
| --- | --- | --- |
| MIN/PTU/serial/software version/supplier accreditation assignment | Pass | OQ-001 covers this. |
| WebPay fiscal terminal identity | Pass | OQ-002 covers this. |
| APM printing of POS Server-issued Sales Invoice | Pass | OQ-003 covers this. |
| Exact VAT/tax treatment | Pass | OQ-004 covers this. |
| Diplomat VAT Privilege / VAT Exemption treatment | Pass | OQ-005 covers this. |
| Diplomat evidence and retention | Pass with improvement | OQ-006 covers this, but should mention VAT Certificate/VAT Identification Card/BIR-DFA evidence candidates. |
| NAAC and Solo Parent report structure activation | Pass | OQ-007 covers this. |
| Offline fiscal issuance | Pass | OQ-008 covers this. |
| DR/restore and counter continuity implementation | Pass | OQ-009 covers this. |
| Sales Invoice/adjustment numbering and reset-counter placement | Gap | This remains open in planning artifacts but is omitted from BRD open questions. |
| X/Z scope and aggregation | Gap | This remains open in planning artifacts but is omitted from BRD open questions. |
| Supplier/applicant/accreditation responsibility | Gap | This remains open in planning artifacts but is omitted from BRD open questions. |

No decided item is incorrectly listed as an open blocker. The issue is missing unresolved items, not stale decided items.

## 11. BRD vs Technical Design Boundary Review

The BRD generally stays within business/control requirement scope. It does not define database tables, columns, endpoint paths, DTOs, event schemas, or storage mechanics.

| Section | Result | Notes |
| --- | --- | --- |
| 14 Functional Requirements | Pass | Business capability language is appropriate. |
| 21 Fiscal Audit, EJ, POSLog, Export, and Retention Requirements | Pass | Technical terms are necessary fiscal outputs, not implementation detail. |
| 23 Business Continuity and Degraded Operation | Pass | Leaves exact offline implementation open. |
| 27 Non-Functional Requirements | Pass | Control-level wording, not design. |
| 33 Requirements Traceability Matrix | Pass with improvement | Appropriate for BRD, but missing several rows. |

No section needs to be moved wholesale to System Design or API Contract. The next BRD edit should avoid adding endpoint/schema mechanics while strengthening business requirements.

## 12. Acceptance Criteria Review

| Required acceptance area | Result | Notes |
| --- | --- | --- |
| Sales Invoice before ExitAuthorization | Pass | AC-001. |
| Fiscal issuance failure blocks ExitAuthorization and triggers exception/retry | Pass | AC-002. |
| WebPay routes to resolved Site POS Server | Pass | AC-003. |
| APM routes to resolved Site POS Server | Pass | AC-004. |
| Cashier POS uses same Site POS Server fiscal authority | Pass | AC-005. |
| EC Device / Continuity Terminal uses same Site POS Server when activated | Pass | AC-006. |
| X-read generation | Pass | AC-007. |
| Z-read close and Z-counter advance | Pass | AC-008. |
| Reset counter starts at zero and increments only on reset | Pass | AC-009. |
| BIR Sales Summary reconciliation | Pass | AC-010. |
| Senior/PWD immediate workflows | Pass | AC-011. |
| NAAC/Solo Parent future-supported categories | Pass | AC-012. |
| Diplomat VAT Privilege / VAT Exemption active treatment | Pass | AC-013. |
| Reprints labeled and audited | Pass | AC-014. |
| Void/refund/cancel/return controls | Pass | AC-015. |
| Fiscal record retention | Pass | AC-016. |
| POS Server does not issue ExitAuthorization | Pass | AC-017. |
| Payment Orchestrator and WebPay do not declare platform finality | Pass | AC-018. |
| Operator-assisted payment routes through Site POS Server | Gap | Add acceptance criterion. |
| Future channel does not become independent POS authority | Gap | Add acceptance criterion. |
| Fiscal identity/footer metadata is supported pending assignment | Gap | Add acceptance criterion or traceability if not acceptance-level. |

## 13. Traceability Matrix Review

The traceability matrix covers most core decisions and requirements. It should be expanded before approval.

| Traceability area | Result | Recommended addition |
| --- | --- | --- |
| Platform-wide scope | Pass | Existing row is adequate. |
| Sales Invoice primary output | Pass | Existing row is adequate. |
| Site-level POS Server | Pass | Existing row is adequate. |
| Central PMS authority | Pass | Existing row is adequate. |
| Fiscal issuance before ExitAuthorization | Pass | Existing row is adequate. |
| Reset vs Z-counter | Pass with improvement | Add reset audit snapshot details and Grand Total Amount continuity. |
| BIR Sales Summary | Pass | Existing row is adequate. |
| Fiscal identity/footer metadata | Gap | Add row for MIN/PTU/serial/software version/supplier accreditation support and related open questions. |
| Sales Invoice and adjustment numbering | Gap | Add row for OQ covering numbering and reset-counter placement. |
| X/Z scope aggregation | Gap | Add row for open X/Z scope. |
| Accreditation sample package | Gap | Add row for final sample set and supplier/applicant responsibility. |
| Operator-assisted and future channels | Gap | Add row or extend platform/channel traceability. |

## 14. Recommended BRD Edits

1. Add BRD open questions for exact Sales Invoice/adjustment numbering, reset-counter display/append behavior, X/Z scope aggregation, and supplier/applicant/accreditation responsibility.
2. Strengthen Sales Invoice requirements to explicitly support taxpayer, Site, MIN, PTU/ATG, serial number, terminal number, software version, supplier accreditation, and footer metadata once assignment is confirmed.
3. Add acceptance criteria for operator-assisted payment and future payment channels under the Site POS Server model.
4. Add a business requirement that fiscal outputs must reconcile from canonical fiscal records so Sales Invoice, EJ, POSLog, X/Z, BIR Sales Summary, Annex E reports, and audit records do not diverge.
5. Strengthen void/refund/cancel/return requirements with original fiscal document linkage and BIR-confirmed reversal/fiscal warning treatment.
6. Expand BIR/export requirements to preserve known candidate outputs while keeping exact formats open.
7. Add RBAC language for segregation between cashier, supervisor, fiscal admin, auditor, and recovery responsibilities.
8. Add Diplomat evidence candidate wording to the open question section without deciding the final required document.
9. Expand risks and traceability for fiscal identity assignment, numbering, X/Z scope, accreditation sample package, and operator-assisted/future channels.
10. Consider removing or updating the Document Control branch field.

## 15. Recommended Next Step

Create a follow-up BRD edit task to address the P1 findings above. The BRD is suitable for targeted revision, not approval yet. No P0 blocker was found, so the next task can be an edit pass rather than a redesign.
