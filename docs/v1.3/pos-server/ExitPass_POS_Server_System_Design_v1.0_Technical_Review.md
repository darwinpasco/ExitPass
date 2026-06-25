# ExitPass POS Server System Design v1.0 Technical Review

## 1. Review Summary

This review checks `ExitPass_POS_Server_System_Design_v1.0.md` against the approved POS/Invoicing BRD baseline, POS Server planning artifacts, and the referenced POS Server diagrams.

The draft preserves the core architecture decisions:

- POS/Invoicing remains platform-wide and is not APM-only.
- The resolved Site determines the Site-level POS Server fiscal authority.
- Channels and terminals are children of the Site POS Server.
- Sales Invoice is the primary parking fiscal output.
- Central PMS owns payment finality and ExitAuthorization.
- POS Server owns fiscal issuance, counters, reports, EJ, POSLog, exports, fiscal audit, reprints, adjustments, retention, and fiscal recovery continuity.
- Fiscal issuance occurs after payment finality and before ExitAuthorization.
- Printed and digital Sales Invoice presentations represent the same fiscal document and fiscal facts.
- Digital SI URL and QR presentation are modeled without making any channel or terminal the fiscal issuer.
- Open compliance, accounting, security, and privacy questions remain visible.

## 2. Overall Recommendation

The POS Server System Design v1.0 draft is technically aligned with the approved BRD and is ready for approval-readiness review.

No P0 authority or core decision violations were found. No P1 should-fix findings were found. The remaining findings are non-blocking improvements and editorial clarity items that can be handled in a targeted cleanup pass or during approval-readiness preparation.

## 3. Blocking Findings

No P0 findings were identified.

| ID | Severity | Section or Diagram | Finding | Why it matters | Recommended correction |
|---|---|---|---|---|---|
| None | P0 | Not applicable | No approval blocker found. | The design preserves the approved BRD authority model and fiscal issuance sequence. | No blocking correction required. |

## 4. Should-Fix Findings

No P1 findings were identified.

| ID | Severity | Section or Diagram | Finding | Why it matters | Recommended correction |
|---|---|---|---|---|---|
| None | P1 | Not applicable | No should-fix issue found before approval-readiness review. | The draft is coherent enough for the next review stage. | No P1 correction required. |

## 5. Non-Blocking Findings

| ID | Severity | Section or Diagram | Finding | Why it matters | Recommended correction |
|---|---|---|---|---|---|
| POS-SD-TR-P2-001 | P2 | Sections 3, 38-40, 43-44 | The draft references source baselines and impact areas but does not include a compact design traceability table from BRD decisions or acceptance themes to system design sections. | The design is reviewable, but approval-readiness will be easier if reviewers can see where each approved BRD decision is implemented or deferred. | Add a short traceability appendix or table mapping major BRD requirements to design sections, diagrams, and open questions. |
| POS-SD-TR-P2-002 | P2 | Section 8 and Diagram PSD-D02 | `QR Presentation Support boundary` is listed with logical POS Server components while the design also correctly says QR presentation is a channel/terminal capability. The wording could be clearer that POS Server supplies URL/metadata and channels perform presentation. | The current text does not violate the BRD, but implementers may confuse POS Server responsibility for URL issuance with terminal responsibility for QR display/printing. | Clarify that POS Server owns digital SI URL generation and presentation rules, while QR rendering/display/print execution is a channel or terminal capability. |
| POS-SD-TR-P2-003 | P2 | Sections 30 and 36 | Offline/degraded fiscal issuance is kept open, but the operational default could be stated more explicitly as restricted/no offline fiscal issuance until BIR/accounting approval. | The BRD already requires offline fiscal issuance to remain restricted. Making the default more explicit reduces implementation drift. | Add one sentence that offline fiscal issuance is disabled or restricted by default until an approved model is defined in POS Server System Design/API/Database follow-on work. |
| POS-SD-TR-P2-004 | P2 | Sections 31, 38, and 40 | Sequence gaps, idempotency, and retry ownership are visible as open questions, but the impact on API contract and eventing could be cross-referenced more tightly. | Idempotency and replay behavior are central to avoiding duplicate fiscal issuance. The draft flags the issue but could make the downstream API/event contract dependency easier to find. | Cross-reference the idempotency open question from the Central PMS integration, eventing, and API contract impact sections. |

## 6. Editorial Findings

| ID | Severity | Section or Diagram | Finding | Why it matters | Recommended correction |
|---|---|---|---|---|---|
| POS-SD-TR-ED-001 | Editorial | Appendix B | EC terminology is defined as `Emergency / Exception / Continuity` and marked pending final terminology. This is acceptable but should remain consistent with later API and operations documents. | Consistent terminology avoids confusion across BRD, system design, operations, and API contract documents. | Keep the pending marker until the term is finalized in v1.3 document set. |
| POS-SD-TR-ED-002 | Editorial | Section 43 | Diagram entries are complete, but the section could optionally include diagram IDs matching the diagram index. | IDs make cross-references easier during review. | Add diagram IDs such as PSD-D01 through PSD-D07 beside diagram headings in a future cleanup pass. |

## 7. BRD Alignment Review

The draft aligns with the approved POS/Invoicing BRD baseline.

| Review item | Result | Notes |
|---|---|---|
| POS/Invoicing is platform-wide | Pass | The design repeatedly states that the POS Server supports WebPay, APM, Cashier POS, EC/continuity, operator-assisted payment, and future channels. |
| Site-level POS Server is fiscal authority | Pass | The resolved Site determines the POS Server issuing the Sales Invoice. |
| Channels/terminals are children of Site POS Server | Pass | The channel and terminal registration model preserves child-channel treatment. |
| Sales Invoice is primary parking fiscal output | Pass | The draft does not introduce Official Receipt as the primary output. |
| Printed and digital SI consistency | Pass | The draft states that printed and digital forms must represent the same fiscal document and fiscal facts. |
| POS Server returns digital SI URL | Pass | The Sales Invoice lifecycle and digital SI sections include this requirement. |
| QR presentation is not APM-only | Pass | The draft covers APM, Cashier POS, EC/continuity, operator-assisted terminals, and future channels where supported. |
| Open items remain visible | Pass | Major compliance, tax, numbering, identity, export, recovery, and security/privacy questions remain open. |

## 8. Authority Boundary Review

No authority boundary violations were found.

The design preserves:

- Central PMS owns payment finality and ExitAuthorization.
- Payment Orchestrator verifies provider outcome but does not declare platform finality.
- WebPay does not declare platform finality.
- POS Server does not issue ExitAuthorization.
- Gate/exit execution does not bypass Central PMS.
- Vendor PMS / HikCentral acknowledgment is synchronization only.
- POS Server owns fiscal issuance, reports, counters, EJ, POSLog, exports, audit trail, reprints, adjustments, retention, and recovery continuity.

## 9. Fiscal Issuance Sequence Review

The system design consistently preserves the required sequence:

1. Payment Orchestrator verifies provider outcome.
2. Payment Orchestrator reports verified outcome to Central PMS.
3. Central PMS records payment finality.
4. Central PMS requests Sales Invoice issuance from the resolved Site POS Server.
5. POS Server issues the Sales Invoice.
6. POS Server returns SI identity/status and digital SI URL where applicable.
7. Central PMS records the fiscal reference.
8. Central PMS issues ExitAuthorization.
9. Gate consumes ExitAuthorization.
10. Vendor PMS acknowledgment remains synchronization only.

No wording was found that permits ExitAuthorization before successful fiscal issuance, except for the separately controlled manual exception path that requires supervision, incident tagging, and reconciliation tagging.

## 10. Component Scope Review

The logical component model is appropriate for a system design draft and does not incorrectly finalize code modules or schemas.

| Component | Result | Notes |
|---|---|---|
| Fiscal Issuance Service | Pass | Correctly scoped around issuance orchestration and idempotency concerns. |
| Sales Invoice Renderer | Pass | Covers printed/digital rendering without final layout over-specification. |
| Digital SI URL Service | Pass | Correctly tied to issued SI and security/privacy open items. |
| QR Presentation Support boundary | Pass with P2 clarification | Needs clearer ownership wording between POS Server rules and terminal presentation execution. |
| Numbering and Counter Service | Pass | Separates fiscal sequences, reset counter, Z-counter, GTA, EJ hash, and fiscal timestamp. |
| X/Z Reporting Service | Pass | Keeps scope/aggregation open for BIR/accounting confirmation. |
| BIR Sales Summary Service | Pass | Treated as required fiscal reporting, not analytics. |
| Annex E Reporting Service | Pass | Extensible reporting treatment is covered. |
| Electronic Journal Service | Pass | Covered as canonical fiscal record output/control. |
| POSLog Export Service | Pass | Covered with final formats open. |
| Fiscal Adjustment Service | Pass | Void/refund/cancel/return controls are included. |
| Reprint Control Service | Pass | Reprint audit/control treatment is included. |
| Fiscal Audit Service | Pass | Audit trail responsibility is clear. |
| Fiscal Retention/Export Service | Pass | Retention/export impact is covered without over-specifying storage. |
| Fiscal Identity / Terminal Registry | Pass | Keeps MIN/PTU/serial/software/supplier assignment open. |
| Security/RBAC boundary | Pass | Role separation and high-risk fiscal controls are described at design level. |

## 11. Digital SI URL and QR Code Review

The design correctly states:

- POS Server returns the digital SI URL.
- The SI URL points to the same issued Sales Invoice as the printed SI.
- QR code presentation is a channel/terminal display or print capability.
- QR presentation is not APM-only.
- QR presentation does not make the terminal/channel the fiscal issuer.
- The Site POS Server remains the fiscal issuer.
- Access policy, expiry policy, authentication/access model, and audit treatment remain open for security/privacy/compliance/API design.

Non-blocking clarification is recommended for QR Presentation Support ownership, as noted in `POS-SD-TR-P2-002`.

## 12. Counters, Numbering, and Recovery Review

The design clearly separates:

- Sales Invoice sequence.
- Adjustment document sequence.
- Reset counter.
- Z-counter.
- Grand Total Amount accumulator.
- Latest EJ hash.
- Last fiscal event timestamp.

The draft correctly states:

- Reset counter starts from zero and increments only on fiscal reset.
- Reset counter does not advance per Z-read.
- Z-counter advances per Z-reading / fiscal day close.
- POS Server must not resume from lower counters, lower Grand Total Amount, lower Z-counter, earlier Sales Invoice sequence, broken EJ hash continuity, or earlier last fiscal event timestamp.
- If continuity cannot be proven, fiscal issuance must be blocked pending supervised recovery and a recovery audit record.

No counter or recovery contradiction was found.

## 13. BIR/POS Compliance Review

The design covers the expected BIR/POS compliance areas at system design level:

- Sales Invoice identity/header/footer metadata.
- MIN/PTU/serial/software/supplier accreditation assignment as open.
- X-read and Z-read.
- BIR Sales Summary.
- Annex E reports / statutory sales books.
- Electronic Journal.
- POSLog.
- Fiscal exports.
- Reprints.
- Void/refund/cancel/return fiscal adjustment documents.
- Accreditation sample set as open.
- Supplier/applicant responsibility as open.

The design does not over-decide open BIR/accounting items. It appropriately leaves fiscal identity assignment, numbering pattern, X/Z scope, export formats, and accreditation package details open for confirmation.

## 14. Entitlement and VAT Privilege Review

The design covers required entitlement and VAT privilege categories:

- Senior Citizen as an immediate operational workflow.
- PWD as an immediate operational workflow.
- NAAC as future-supported.
- Solo Parent as future-supported.
- Diplomat VAT Privilege / VAT Exemption as active.

The design correctly treats Diplomat VAT Privilege / VAT Exemption as a VAT privilege/exemption based on RMO No. 10-2019, not as an ordinary commercial discount. Exact evidence, retention, wording, reporting, and validation details remain open for BIR/accounting and security/privacy confirmation.

## 15. API and Database Over-Specification Review

No API or database over-specification was found.

The design does not finalize:

- Database tables.
- Columns.
- Indexes.
- Constraints.
- Migrations.
- API endpoint paths.
- DTOs.
- Event schemas.
- Exact payloads.
- Final status code models.

Sections 38, 39, and 40 identify eventing, database, and API impact areas without turning the document into a contract or schema design. Candidate events and data areas are framed as impact topics and downstream design inputs, which is appropriate.

## 16. Open Questions Review

The Open Questions section carries forward the major unresolved items and classifies them by downstream decision area.

Visible open questions include:

- Fiscal identity assignment.
- WebPay fiscal terminal identity.
- APM printing model.
- VAT/tax treatment.
- Diplomat VAT evidence/reporting.
- Offline fiscal issuance.
- Recovery continuity implementation.
- Export formats.
- Sales Invoice and adjustment numbering.
- X/Z scope and aggregation.
- Supplier/applicant responsibility.
- Digital SI URL access controls.
- Non-APM QR rules if mandatory.
- Sequence gaps and idempotency.
- Tamper-evident anchoring.
- Fiscal roles and permissions.
- Clock authority and rollback controls.
- Entitlement/evidence storage.

No decided item was found incorrectly reopened as a blocker. No major open item from the BRD/planning set appears to have been silently decided.

## 17. Diagram Review

| Diagram | JPEG/source check | Authority check | Review result |
|---|---|---|---|
| POS Server Context and Authority Boundary | JPEG and PlantUML paths are referenced under `diagrams/`. | Pass | Shows Central PMS authority and POS Server fiscal ownership without showing POS Server issuing ExitAuthorization. |
| POS Server Component Architecture | JPEG and PlantUML paths are referenced under `diagrams/`. | Pass | Business-readable logical component view; does not imply final modules or schemas. QR support boundary could be clarified in text. |
| Payment Finality to SI to ExitAuthorization | JPEG and PlantUML paths are referenced under `diagrams/`. | Pass | Correctly shows fiscal issuance before ExitAuthorization and Vendor PMS acknowledgment as synchronization. |
| Digital SI URL and QR Code Presentation Model | JPEG and PlantUML paths are referenced under `diagrams/`. | Pass | Correctly shows QR presentation as a channel/terminal capability and includes customer phone view/save behavior. |
| Fiscal Output and Reporting Pipeline | JPEG and PlantUML paths are referenced under `diagrams/`. | Pass | Correctly shows canonical fiscal records feeding SI, EJ, POSLog, reports, exports, audit, reprints, and adjustments. |
| Fiscal Counters and Recovery Continuity Model | JPEG and PlantUML paths are referenced under `diagrams/`. | Pass | Correctly shows counter continuity, GTA, EJ hash, fiscal timestamp, supervised recovery gate, and recovery audit record. |
| Fiscal Issuance Failure and Retry Flow | JPEG and PlantUML paths are referenced under `diagrams/`. | Pass | Correctly blocks ExitAuthorization on SI failure except controlled supervisor-approved exception path. |

The diagrams are business-readable and do not show channels as independent POS systems. No diagram contradicts the authority model.

## 18. Traceability / Impact Review

The design includes impact sections for:

- Eventing and outbox.
- Database design.
- API contract.
- Observability and operations.
- Testing and certification.
- Risks and mitigations.

The impact treatment is appropriate and avoids premature API/database design. A compact traceability matrix would improve approval-readiness but is not a blocker.

## 19. Recommended Targeted Edits

Recommended non-blocking edits:

1. Add a compact traceability appendix mapping approved BRD decisions and acceptance themes to POS Server System Design sections, diagrams, and open questions.
2. Clarify QR Presentation Support ownership: POS Server owns URL issuance and presentation rules; channels/terminals perform QR display or print where supported.
3. State the default offline fiscal issuance posture more explicitly as restricted until BIR/accounting approval.
4. Add cross-references for idempotency and sequence-gap handling from Central PMS integration, eventing/outbox, and API contract impact sections.
5. Optionally add diagram IDs in Section 43 to match the diagram index.

## 20. Recommended Next Step

Proceed to approval-readiness review, or perform a short targeted cleanup pass for the P2/editorial findings first if the documentation set needs stronger reviewer traceability before stakeholder circulation.

Recommended next Codex Z task: apply a focused system design cleanup pass for traceability, QR ownership wording, offline fiscal issuance default wording, and idempotency cross-references, then run approval-readiness review.
