# ExitPass POS/Invoicing BRD v1.0 Approval-Readiness Review

## 1. Review Summary

| Field | Value |
| --- | --- |
| Review target | `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` |
| Diagram folder | `docs/v1.3/pos-invoicing/diagrams/` |
| Review baseline | Prior BRD review, decision log, decision recommendations, open questions, source analysis, and POS Server impact map |
| Review date | 2026-06-25 |
| Review type | Documentation approval-readiness review only |
| Outcome | Ready for stakeholder review / approval, with non-blocking follow-up recommendations |

The BRD now reflects the core POS/Invoicing decisions and the targeted follow-up additions. It presents POS/Invoicing as a platform-wide capability, keeps Sales Invoice as the primary parking fiscal output, preserves the Site-level POS Server model, protects the Central PMS authority boundary, requires fiscal issuance before ExitAuthorization, distinguishes reset counter from Z-counter, covers BIR Sales Summary and Annex E reporting, includes entitlement and VAT privilege categories, adds digital Sales Invoice delivery requirements, and provides Appendix C diagrams with valid JPEG and PlantUML references.

No P0 approval blockers or P1 should-fix-before-approval findings were found.

## 2. Approval Recommendation

Recommendation: proceed to stakeholder review / approval.

The document is approval-ready for BRD purposes. Remaining unresolved items are correctly framed as open questions for BIR/accounting confirmation, POS Server System Design, security/privacy review, or implementation planning. The BRD does not need another targeted edit pass before stakeholder review unless reviewers want optional diagram refinements.

## 3. Blocking Findings

No P0 findings were found.

| ID | Severity | Section or diagram | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P0 | Not applicable | No approval-blocking findings were identified. | The BRD preserves the approved fiscal architecture and authority model. | No correction required before stakeholder review. |

## 4. Non-Blocking Findings

| ID | Severity | Section or diagram | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| NBF-001 | P2 | Appendix C diagrams, especially C-01, C-04, and C-05 | The diagrams do not yet visualize the newly added digital Sales Invoice URL and QR-code delivery path. | The BRD text, acceptance criteria, and traceability cover digital delivery, so this is not an approval blocker. A future diagram refresh would make the new delivery requirement easier to communicate visually. | In a later diagram refresh, optionally add a lightweight digital SI URL / QR presentation note to the context, routing, or fiscal output diagram. |
| NBF-002 | P2 | 32 Acceptance Criteria | Acceptance criteria explicitly mention APM QR presentation but do not separately test Cashier POS, EC/continuity, and operator-assisted QR display/print, even though those channel requirements are present as "may" requirements. | This is acceptable for BRD approval because non-APM QR use is optional/channel-dependent, but implementation test planning may benefit from channel-specific criteria if those options become committed scope. | If Cashier POS, EC/continuity, or operator-assisted QR delivery becomes mandatory, add channel-specific acceptance criteria in the implementation test plan or a later BRD revision. |

## 5. Editorial Findings

| ID | Severity | Section or diagram | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| ED-001 | Editorial | Appendix C | Appendix C is correctly linked, but diagram images are JPEG exports generated from PlantUML-rendered PNGs because the local PlantUML build did not natively emit JPEG. | The final deliverables are valid `.jpg` files and links work; this is only useful provenance for maintainers. | No BRD correction required. Keep source `.puml` files as the maintainable diagram source. |

## 6. Decision Coverage Review

| Decision area | Result | Notes |
| --- | --- | --- |
| Platform-wide POS/Invoicing scope | Pass | Executive Summary, Scope, Site-level POS Server Model, Channel-Specific Requirements, and traceability all state POS/Invoicing is platform-wide and not APM-only. |
| Sales Invoice as primary parking fiscal output | Pass | Sales Invoice is consistently the primary output. No Official Receipt language is used as the main fiscal output. |
| Site-level POS Server model | Pass | The BRD states one Site-level POS Server per Site or parking operation boundary, with resolved Site determining fiscal issuer. |
| Channels/terminals under Site POS Server | Pass | WebPay, APM, Cashier POS, EC/continuity, operator-assisted payment, and future channels are modeled as child channels/terminals. |
| Central PMS / POS Server authority split | Pass | Central PMS owns payment finality and ExitAuthorization. POS Server owns fiscal issuance, fiscal counters, EJ, POSLog, reporting, and fiscal audit. |
| Fiscal issuance before ExitAuthorization | Pass | Standard flow, functional requirements, exception handling, and acceptance criteria all require fiscal issuance before ExitAuthorization. |
| Reset counter vs Z-counter | Pass | Reset counter starts at zero and increments only on fiscal reset. Z-counter advances per Z-reading / fiscal day close. |
| BIR Sales Summary and Annex E | Pass | BIR Sales Summary is first-class and Annex E structures cover Senior, PWD, NAAC, and Solo Parent requirements. |
| Entitlement and VAT privilege model | Pass | Senior/PWD are immediate, NAAC/Solo Parent are future-supported, and Diplomat VAT Privilege / VAT Exemption is active and not treated as ordinary discount. |

## 7. Digital Sales Invoice Delivery Review

| Review item | Result | Evidence |
| --- | --- | --- |
| Printed and digital Sales Invoice presentation | Pass | `SI-012`, `AC-027`, and traceability row "Sales Invoice printed and digital presentation". |
| POS Server-returned digital Sales Invoice URL | Pass | `SI-013`, `AC-028`, and traceability row "Digital Sales Invoice URL". |
| Parker/customer can view and save SI on phone | Pass | `SI-013` and `AC-029`. |
| APM QR code for digital SI URL | Pass | `CH-APM-006`, `AC-030`, and traceability row "APM QR code for digital Sales Invoice URL". |
| Cashier, EC/continuity, and operator-assisted QR support | Pass | `CH-CASH-005`, `CH-EC-005`, and `CH-OP-005` cover optional channel presentation. |
| Printed/digital consistency | Pass | `SI-014`, `SI-015`, `AUD-012`, `AC-031`, and traceability row "Printed/digital Sales Invoice consistency". |
| Security/privacy/retention/anti-tampering controls | Pass | `PRIV-008` through `PRIV-011`, `AUD-012`, `AUD-013`, `AC-032`, and `OQ-017`. |
| Open access policy details | Pass | `OQ-017` correctly leaves URL access policy, expiry, authentication/access model, and audit treatment to POS Server System Design and compliance confirmation. |

## 8. Authority Boundary Review

| Authority check | Result | Notes |
| --- | --- | --- |
| Central PMS owns payment finality | Pass | Sections 2, 9, 12, 13, 14, and 24 preserve this. |
| Central PMS owns ExitAuthorization | Pass | Sections 12, 13, 14, 22, and `AC-017` preserve this. |
| POS Server does not issue ExitAuthorization | Pass | Explicitly stated in Section 12, `FR-005`, `SEC-003`, and `AC-017`. |
| Payment Orchestrator does not declare platform finality | Pass | Covered by Section 12, `FR-006`, `SEC-004`, and `AC-018`. |
| WebPay does not declare platform finality | Pass | Covered by `CH-WP-002`, `SEC-004`, and `AC-018`. |
| Gate/exit does not bypass Central PMS | Pass | Section 12 and channel requirements preserve gate authorization boundaries. |
| Fiscal issuance before ExitAuthorization | Pass | The standard sequence and `AC-001` / `AC-002` are consistent. |
| Diagrams preserve authority model | Pass | No diagram shows POS Server issuing ExitAuthorization, channels as independent POS systems, or WebPay/Payment Orchestrator declaring platform finality. |

## 9. BIR/POS Compliance Review

| Area | Result | Notes |
| --- | --- | --- |
| Sales Invoice output | Pass | Sales Invoice is primary parking fiscal output. Adjustment documents are treated as related fiscal workflows. |
| Sales Invoice identity/header/footer metadata | Pass | `SI-008` through `SI-011` support BIR-required metadata while keeping field assignment open. |
| X-read / Z-read | Pass | X/Z capabilities and scope open question are documented. |
| Reset counter / Z-counter | Pass | The BRD clearly separates reset counter and Z-counter behavior. |
| BIR Sales Summary | Pass | Treated as first-class required fiscal report, not optional analytics. |
| Annex E / statutory sales books | Pass | Senior/PWD immediate and NAAC/Solo Parent future-supported structures are covered. |
| EJ, POSLog, exports, and audit | Pass | Canonical reconciliation and export requirements are present. |
| Reprints and adjustment controls | Pass | Reprint, void, refund, cancel, return, original-document linkage, and audit requirements are present. |
| Digital SI controls | Pass | URL security, privacy, anti-tampering, retention, and access model open questions are documented. |

## 10. Channel Coverage Review

| Channel | Result | Notes |
| --- | --- | --- |
| WebPay | Pass | Routes to Site POS Server, does not declare finality, supports digital SI presentation. |
| AutoPay Machine / APM | Pass | Routes to Site POS Server, remains child terminal/channel, supports QR code for digital SI URL. |
| Cashier POS | Pass | Uses Site POS Server authority and may display/print digital SI QR code. |
| EC Device / Continuity Terminal | Pass | Uses Site POS Server authority when activated and may display/print digital SI QR code under approved continuity model. |
| Operator-assisted payment | Pass | Routes to Site POS Server if allowed and may display/print digital SI QR code. |
| Future payment channels | Pass | Must register as child channels/terminals and not become independent POS systems. |

## 11. Diagram Review

| Appendix entry | JPEG link | PlantUML source | Review result |
| --- | --- | --- | --- |
| C-01 POS/Invoicing Context Diagram | Exists: `diagrams/ExitPass_POS_Invoicing_Context_Diagram.jpg` | Exists: `diagrams/ExitPass_POS_Invoicing_Context_Diagram.puml` | Pass. Title and purpose match. Shows Parker, channels, Payment Orchestrator, Central PMS, Site POS Server, Vendor PMS/HikCentral, BIR outputs, and finance/audit/compliance users. Authority notes are correct. |
| C-02 Site-level POS Server Model | Exists: `diagrams/ExitPass_Site_Level_POS_Server_Model.jpg` | Exists: `diagrams/ExitPass_Site_Level_POS_Server_Model.puml` | Pass. Shows Site Group, resolved Site, Site POS Server, child channels/terminals, future channels, Sales Invoice issuance, and BIR reports scoped to Site POS Server. Does not model channels as independent POS systems. |
| C-03 Payment-to-Exit Fiscal Sequence | Exists: `diagrams/ExitPass_Payment_to_Exit_Fiscal_Sequence.jpg` | Exists: `diagrams/ExitPass_Payment_to_Exit_Fiscal_Sequence.puml` | Pass. Shows provider verification, Central PMS recording payment finality, POS Server issuing Sales Invoice, Central PMS recording fiscal reference, then issuing ExitAuthorization. Vendor acknowledgment is shown as synchronization only. |
| C-04 Channel / Terminal Fiscal Routing Diagram | Exists: `diagrams/ExitPass_Channel_Terminal_Fiscal_Routing.jpg` | Exists: `diagrams/ExitPass_Channel_Terminal_Fiscal_Routing.puml` | Pass. Shows channels routing context through Central PMS Site resolution to resolved Site POS Server. The payment channel does not decide fiscal authority. |
| C-05 Fiscal Output and Reporting Model | Exists: `diagrams/ExitPass_Fiscal_Output_Reporting_Model.jpg` | Exists: `diagrams/ExitPass_Fiscal_Output_Reporting_Model.puml` | Pass. Shows Sales Invoice, EJ, POSLog, X/Z, BIR Sales Summary, Annex E, exports, audit, counters, Grand Total Amount, reprints, and adjustments. It correctly states printed outputs are simplified and canonical detail remains digital. |
| C-06 Fiscal Issuance Failure Exception Flow | Exists: `diagrams/ExitPass_Fiscal_Issuance_Failure_Exception_Flow.jpg` | Exists: `diagrams/ExitPass_Fiscal_Issuance_Failure_Exception_Flow.puml` | Pass. Shows payment finality, fiscal issuance failure/timeout, blocked ExitAuthorization, exception/retry, messaging, supervisor approval if allowed, incident/reconciliation tagging, and controlled closure. It does not reverse payment automatically. |

Diagram authority checks:

- No diagram shows POS Server issuing ExitAuthorization.
- No diagram shows WebPay or Payment Orchestrator declaring platform payment finality.
- No diagram shows channels as independent POS systems.
- Payment-to-exit and exception diagrams preserve fiscal issuance before ExitAuthorization.
- Diagrams are business-readable and use clear labels, notes, and simple directionality.

## 12. Open Questions Review

| Open question area | Result | Notes |
| --- | --- | --- |
| MIN/PTU/serial/software/supplier assignment | Pass | `OQ-001` preserves this unresolved compliance item. |
| WebPay fiscal terminal identity | Pass | `OQ-002` remains open. |
| APM printing model | Pass | `OQ-003` remains open. |
| Exact VAT/tax treatment | Pass | `OQ-004` remains open. |
| Diplomat VAT Privilege / VAT Exemption details | Pass | `OQ-005` and `OQ-006` remain open. |
| NAAC and Solo Parent report structure activation | Pass | `OQ-007` remains open. |
| Offline fiscal issuance | Pass | `OQ-008` remains open. |
| DR/restore continuity implementation | Pass | `OQ-009` remains open. |
| Export formats | Pass | `OQ-010` remains open. |
| Accreditation samples and supplier/applicant split | Pass | `OQ-011` and `OQ-016` remain open. |
| Numbering and reset-counter display | Pass | `OQ-012` through `OQ-014` remain open. |
| X/Z scope | Pass | `OQ-015` remains open. |
| Digital SI URL access model | Pass | `OQ-017` is correctly added and scoped to System Design, security, privacy, and compliance. |

No decided item appears to be incorrectly left as an open blocker. The remaining questions are genuinely unresolved and appropriately scoped outside BRD approval.

## 13. Acceptance Criteria Review

| Acceptance area | Result | Notes |
| --- | --- | --- |
| Sales Invoice before ExitAuthorization | Pass | `AC-001`, `AC-002`. |
| WebPay, APM, Cashier POS, EC/continuity routing | Pass | `AC-003` through `AC-006`. |
| X-read, Z-read, reset counter | Pass | `AC-007` through `AC-009`. |
| BIR Sales Summary reconciliation | Pass | `AC-010`. |
| Senior/PWD immediate workflows | Pass | `AC-011`. |
| NAAC/Solo Parent future-supported categories | Pass | `AC-012`. |
| Diplomat VAT Privilege / VAT Exemption active treatment | Pass | `AC-013`. |
| Reprint and adjustment controls | Pass | `AC-014`, `AC-015`, `AC-026`. |
| Fiscal retention, authority boundaries, and output consistency | Pass | `AC-016` through `AC-020`, `AC-024`. |
| Operator-assisted and future channels | Pass | `AC-021`, `AC-022`. |
| Sales Invoice identity/header/footer | Pass | `AC-023`. |
| X/Z approved scope | Pass | `AC-025`. |
| Digital Sales Invoice delivery | Pass | `AC-027` through `AC-032`. |

Acceptance criteria are complete enough for BRD approval. NBF-002 notes a possible later refinement if optional non-APM QR delivery becomes mandatory implementation scope.

## 14. Requirements Traceability Review

| Traceability area | Result | Notes |
| --- | --- | --- |
| Platform scope and channel model | Pass | Rows cover platform-wide scope, operator-assisted channel, future channels, Site-level POS Server, and channel routing. |
| Sales Invoice fiscal output | Pass | Rows cover primary SI output, SI identity/header/footer, printed/digital presentation, and digital URL. |
| Digital SI delivery | Pass | Rows cover digital SI URL, APM QR code, printed/digital SI consistency, and digital SI access control/retention/audit. |
| Authority model | Pass | Rows cover Central PMS authority and fiscal issuance before ExitAuthorization. |
| Counters and fiscal continuity | Pass | Rows cover reset vs Z-counter, reset audit snapshot, Grand Total Amount continuity, DR/restore continuity, and open reset display behavior. |
| Reports and exports | Pass | Rows cover BIR Sales Summary, EJ, POSLog, export, retention, canonical reconciliation, and Annex E through entitlement/reporting rows. |
| Entitlements and VAT privilege | Pass | Rows cover entitlement model, fiscal line classification, Diplomat evidence handling, and open tax/Diplomat details. |
| Open compliance questions | Pass | Rows cover MIN/PTU/serial assignment, numbering, X/Z scope, accreditation package, supplier/applicant responsibility, tax/Diplomat details, and offline issuance. |

Traceability is adequate for approval-readiness. No missing traceability row was found for the current BRD scope.

## 15. Final Recommendation

The BRD is ready for stakeholder review / approval.

No P0 or P1 findings were identified. The remaining findings are non-blocking P2 or editorial items. They can be handled in a future diagram refresh, implementation test planning, or POS Server System Design without delaying BRD stakeholder review.

## 16. Recommended Next Step

Proceed with stakeholder review / approval of `ExitPass_POS_Invoicing_BRD_v1.0.md`.

After BRD approval, use the open questions to drive follow-up workstreams:

- POS Server System Design.
- POS Server API Contract.
- BIR/accounting confirmation package.
- Security/privacy review for digital Sales Invoice URL access.
- Optional diagram refresh to add digital SI URL / QR presentation if stakeholders want that visualized.
