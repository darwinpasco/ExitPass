# ExitPass POS Server System Design v1.0 Approval-Readiness Review

## 1. Review Summary

This review assesses `ExitPass_POS_Server_System_Design_v1.0.md` after the technical review and targeted cleanup pass.

The System Design is aligned with the approved POS/Invoicing BRD v1.0 and preserves the required authority model. The targeted cleanup findings from the technical review have been addressed in the current draft:

- Compact BRD-to-System Design traceability is present in Appendix C.
- QR code presentation ownership is clarified as POS Server URL/rule ownership plus channel/terminal display or print execution.
- Offline fiscal issuance is disabled or restricted by default until BIR/accounting approval.
- Idempotency, retry, sequence-gap, and reserved-number concerns are cross-referenced across lifecycle, numbering, exception handling, Central PMS integration, eventing/outbox, API impact, and open questions.
- Diagram headings use `PSD-D01` through `PSD-D07`.

## 2. Approval Recommendation

Recommendation: Ready for architecture/stakeholder review and approval as the POS Server System Design v1.0 baseline, subject to the open questions remaining explicitly deferred to BIR/accounting, security/privacy, POS Server API Contract, POS Server Database Design, and Engineering Pack follow-on work.

No P0 approval blockers were found. No P1 should-fix findings were found.

## 3. Blocking Findings

No P0 findings were identified.

| ID | Severity | Section or diagram | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P0 | Not applicable | No approval blocker found. | The design preserves the approved BRD authority model, fiscal sequence, Site-level POS Server boundary, and open-question posture. | No blocking correction required. |

## 4. Should-Fix Findings

No P1 findings were identified.

| ID | Severity | Section or diagram | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P1 | Not applicable | No should-fix issue found before approval. | The document is ready for stakeholder and architecture review. | No P1 correction required. |

## 5. Non-Blocking Findings

No P2 findings were identified.

| ID | Severity | Section or diagram | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P2 | Not applicable | No non-blocking correction found in this review. | The prior P2 cleanup items were addressed. | No P2 correction required. |

## 6. Editorial Findings

No editorial findings were identified.

| ID | Severity | Section or diagram | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | Editorial | Not applicable | No editorial correction found in this review. | The document is readable and uses consistent diagram IDs and open-question labels. | No editorial correction required. |

## 7. BRD Alignment Review

| Review item | Result | Evidence |
| --- | --- | --- |
| Approved POS/Invoicing BRD baseline preserved | Pass | Sections 2-4 and Appendix C map the design back to approved BRD decisions. |
| Site-level POS Server boundary | Pass | Sections 5-6 define Site POS Server as Site-level fiscal authority for the resolved Site. |
| Channels/terminals under Site POS Server | Pass | Section 9 and Appendix C treat WebPay, APM, Cashier POS, EC/continuity, operator-assisted payment, and future channels as child channels/terminals. |
| Sales Invoice lifecycle | Pass | Section 11 defines the lifecycle from Central PMS request through POS Server issuance, digital SI URL generation, fiscal reference return, and Central PMS ExitAuthorization. |
| Printed and digital SI consistency | Pass | Section 12 requires printed and digital SI forms to represent the same fiscal document and fiscal facts. |
| Digital SI URL | Pass | Sections 11-13 state that POS Server creates/returns the digital SI URL after SI issuance where applicable. |
| QR code presentation | Pass | Sections 8, 13, and 33-37 state QR presentation is a channel/terminal capability, not APM-only. |
| Offline fiscal issuance default | Pass | Sections 30 and 36 explicitly restrict/disable offline fiscal issuance by default pending BIR/accounting approval. |

## 8. Authority Boundary Review

The System Design preserves the authority split.

| Authority area | Result | Evidence |
| --- | --- | --- |
| Central PMS payment finality | Pass | Sections 4, 7, 11, 31, and Appendix C keep payment finality with Central PMS. |
| Central PMS ExitAuthorization | Pass | Sections 4, 7, 11, 30, 31, and 34-37 keep ExitAuthorization with Central PMS. |
| POS Server does not issue ExitAuthorization | Pass | Sections 5, 7, 30, 31, and diagrams PSD-D01/PSD-D03/PSD-D07 state or show this boundary. |
| Payment Orchestrator authority | Pass | Section 32 limits Payment Orchestrator to provider outcome verification/reporting and blocks platform finality declaration. |
| WebPay authority | Pass | Section 33 blocks WebPay from declaring platform finality, issuing ExitAuthorization, or acting as independent POS system. |
| Gate/exit authority | Pass | Sections 7 and 31 preserve Central PMS authorization as the gate execution basis. |
| Vendor PMS / HikCentral | Pass | Diagram PSD-D03 and related context treat vendor acknowledgment as synchronization only. |

## 9. Fiscal Issuance Sequence Review

The required sequence is preserved:

1. Payment Orchestrator verifies provider outcome.
2. Payment Orchestrator reports verified outcome to Central PMS.
3. Central PMS records payment finality.
4. Central PMS requests SI issuance from the resolved Site POS Server.
5. POS Server validates and issues the Sales Invoice.
6. POS Server returns SI identity/status and digital SI URL where applicable.
7. Central PMS records the fiscal reference.
8. Central PMS issues ExitAuthorization.
9. Gate consumes ExitAuthorization.
10. Vendor PMS acknowledgment remains synchronization only.

No wording was found that allows POS Server, Payment Orchestrator, WebPay, APM, Cashier POS, EC/continuity terminal, operator-assisted flow, or vendor PMS to bypass Central PMS authority.

## 10. Digital SI URL and QR Code Review

| Review item | Result | Evidence |
| --- | --- | --- |
| POS Server returns digital SI URL | Pass | Sections 11-13 and 40. |
| Printed and digital SI represent same fiscal facts | Pass | Section 12 and diagram PSD-D04. |
| QR presentation is channel/terminal capability | Pass | Sections 8, 13, and diagram PSD-D04. |
| QR presentation is not APM-only | Pass | Sections 13, 34-37 and diagram PSD-D04 include APM, Cashier POS, EC/continuity, operator-assisted terminals, and future channels. |
| QR presentation does not create fiscal authority | Pass | Sections 8 and 13 state terminal/channel QR presentation does not make the terminal fiscal issuer. |
| Access model remains open | Pass | Sections 13, 25, 27, 40, and `PSD-OQ-016` keep URL access, expiry, authentication/access model, and audit treatment open for security/privacy/API design. |

## 11. Counter, Numbering, and Recovery Review

The design clearly separates:

- Sales Invoice sequence.
- Adjustment document sequence.
- Reset counter.
- Z-counter.
- Grand Total Amount accumulator.
- Latest EJ hash.
- Last fiscal event timestamp.

Result: Pass.

Evidence:

- Section 14 keeps Sales Invoice numbering, adjustment numbering, reset-counter display/append behavior, reserved numbers, failed issuance, abandoned issuance, and sequence-gap treatment open for BIR/accounting and API design.
- Section 18 states reset counter starts from zero and increments only on fiscal reset; Z-counter advances per Z-reading / fiscal day close.
- Sections 28-29 require tamper-evident fiscal state and prohibit recovery from lower counters, lower Grand Total Amount, lower Z-counter, earlier SI sequence, broken EJ hash continuity, or earlier fiscal event timestamp.
- Section 30 and `PSD-OQ-018` preserve idempotency and sequence-gap handling as an open API/BIR-accounting item.

## 12. BIR/POS Compliance Review

| Compliance area | Result | Evidence |
| --- | --- | --- |
| Sales Invoice identity/header/footer metadata | Pass | Section 10. |
| MIN/PTU/serial/software/supplier assignment | Pass | Section 10 and `PSD-OQ-004` keep assignment open. |
| X-read and Z-read | Pass | Section 17 keeps scope/aggregation open while defining core behavior. |
| BIR Sales Summary and Annex E | Pass | Section 19 treats BIR Sales Summary as first-class reporting and includes Annex E structures. |
| EJ and POSLog | Pass | Sections 20-21. |
| Fiscal exports | Pass | Section 22 keeps final formats open. |
| Reprints | Pass | Section 23 controls reprint labeling/audit and keeps exact label/layout open. |
| Void/refund/cancel/return adjustments | Pass | Section 24 controls linkage, audit, and sequencing while leaving document types/numbering open. |
| Entitlement/VAT privilege handling | Pass | Section 16 covers Senior/PWD immediate workflows, NAAC/Solo Parent future-supported structures, and active Diplomat VAT Privilege / VAT Exemption. |
| Accreditation sample set | Pass | Section 42 and `PSD-OQ-014` keep final sample set open. |
| Supplier/applicant responsibility | Pass | Section 10 and `PSD-OQ-015` keep responsibility open. |

## 13. API and Database Boundary Review

The document stays within system design scope.

| Boundary | Result | Evidence |
| --- | --- | --- |
| No final database schema | Pass | Sections 2 and 39 explicitly avoid final tables, columns, indexes, constraints, and migrations. |
| No final API contract | Pass | Sections 2 and 40 explicitly avoid endpoint paths, DTOs, schemas, status codes, and final error models. |
| No final event schema | Pass | Section 38 identifies eventing/outbox impact without final event names, payloads, delivery guarantees, or replay model. |
| No implementation internals | Pass | Logical components are identified in Section 8 as design components, not final code modules. |
| Open API/database dependencies visible | Pass | Sections 9, 14, 30, 31, 38-40, 44, and Appendix C cross-reference downstream API/database decisions. |

## 14. Open Questions Review

Open questions are visible and correctly treated as unresolved where appropriate.

Key open items retained:

- Sales Invoice numbering.
- Adjustment document numbering.
- Reset-counter display/append behavior.
- MIN/PTU/serial/software/supplier accreditation assignment.
- WebPay fiscal identity.
- APM printing model.
- X-read and Z-read scope.
- Offline fiscal issuance.
- Refund/void sequencing with Central PMS/provider authority.
- VAT/tax treatment.
- Diplomat VAT Privilege / VAT Exemption treatment, evidence, wording, reporting, and retention.
- NAAC/Solo Parent report activation.
- Export formats.
- Accreditation sample set.
- Supplier/applicant responsibility.
- Digital SI URL access policy, expiry, authentication/access model, and audit treatment.
- Non-APM QR mandatory rules.
- Sequence gaps, reserved numbers, failed issuance, and retry idempotency.
- Tamper-evident anchoring and recovery procedure.
- Fiscal roles/permissions, clock authority, and entitlement/evidence data location.

No decided item was found incorrectly reopened as a blocker. No unresolved compliance/accounting/security/privacy item was silently decided.

## 15. Diagram Review

All seven referenced diagrams have matching JPEG and PlantUML source files under `docs/v1.3/pos-server/diagrams/`.

| Diagram | Link/source result | Authority result | Readiness result |
| --- | --- | --- | --- |
| PSD-D01 POS Server Context and Authority Boundary | Pass | Pass | Shows Central PMS payment finality/ExitAuthorization and Site POS Server fiscal issuance/reporting. |
| PSD-D02 POS Server Component Architecture | Pass | Pass | Logical component view is readable and does not define final modules, schemas, DTOs, or endpoints. |
| PSD-D03 Payment Finality to SI to ExitAuthorization Sequence | Pass | Pass | Correctly shows fiscal issuance after payment finality and before ExitAuthorization; vendor sync is not authority. |
| PSD-D04 Digital SI URL and QR Code Presentation Model | Pass | Pass | Shows QR presentation across APM, Cashier POS, EC/continuity, operator-assisted terminal, and future channels; not APM-only. |
| PSD-D05 Fiscal Output and Reporting Pipeline | Pass | Pass | Shows canonical fiscal records feeding SI, EJ, POSLog, X/Z, BIR Summary, Annex E, exports, audit, reprints, and adjustments. |
| PSD-D06 Fiscal Counters and Recovery Continuity Model | Pass | Pass | Shows SI sequence, adjustment sequence, reset counter, Z-counter, GTA, EJ hash, timestamp, recovery gate, and audit record. |
| PSD-D07 Fiscal Issuance Failure and Retry Flow | Pass | Pass | Blocks ExitAuthorization on fiscal issuance failure unless controlled exception is approved. |

No diagram shows POS Server issuing ExitAuthorization. No diagram shows WebPay or Payment Orchestrator declaring platform payment finality. No diagram shows channels as independent POS systems.

## 16. Traceability Review

Appendix C provides a compact BRD-to-System Design traceability map. It covers the required approval themes:

- Platform-wide POS/Invoicing scope.
- Site-level POS Server model.
- Channels/terminals as children.
- Central PMS payment finality and ExitAuthorization authority.
- POS Server fiscal authority.
- Sales Invoice lifecycle.
- Fiscal issuance before ExitAuthorization.
- Printed/digital SI consistency.
- Digital SI URL.
- QR as channel/terminal capability.
- Reset counter vs Z-counter.
- Grand Total Amount, EJ hash, and recovery continuity.
- X-read/Z-read.
- BIR Sales Summary and Annex E.
- EJ and POSLog.
- Reprints and adjustments.
- Security/RBAC and segregation of duties.
- Privacy/evidence and digital SI URL access.
- Open numbering, fiscal identity, export, X/Z scope, and recovery questions.

Result: Pass.

## 17. Final Recommendation

Final recommendation: approve the System Design for architecture/stakeholder review and use it as the POS Server System Design v1.0 baseline, with the explicit understanding that the listed open questions must be resolved in the appropriate downstream workstreams before implementation.

Approval should not be interpreted as approval of final API endpoints, DTOs, event schemas, database tables, fiscal numbering patterns, offline fiscal issuance, MIN/PTU/serial assignment, export formats, or digital SI URL security model. Those remain controlled follow-on decisions.

## 18. Recommended Next Step

Recommended next step: mark `ExitPass_POS_Server_System_Design_v1.0.md` as approved baseline after stakeholder/architecture acceptance, then proceed with POS Server API Contract planning and POS Server Database Design planning using the open questions and Appendix C traceability as inputs.
