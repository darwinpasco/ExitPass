# ExitPass POS Server System Design Decision Log

Version: v1.0 planning artifact
Status: Draft for system design planning only
Generated: 2026-06-25

## Purpose

This decision log separates approved BRD decisions, proposed technical defaults that are safe for planning, pending technical decisions, and non-decisions. It does not finalize open compliance, accounting, security, privacy, database, or API decisions.

## Approved BRD Decisions Carried Forward

| ID | Decision | Status | Source | System Design impact |
| --- | --- | --- | --- | --- |
| PSD-BRD-001 | POS/Invoicing is platform-wide, not APM-only. | Approved baseline | POS/Invoicing BRD v1.0 | POS Server serves all applicable channels for a resolved Site. |
| PSD-BRD-002 | Use one Site-level POS Server per Site or parking operation boundary. | Approved baseline | POS/Invoicing BRD v1.0 | Site is the fiscal boundary for issuance, counters, reports, and audit. |
| PSD-BRD-003 | Channels/terminals are children of the Site POS Server. | Approved baseline | POS/Invoicing BRD v1.0 | Terminal/channel registry is needed. |
| PSD-BRD-004 | Parking payment fiscal output is Sales Invoice. | Approved baseline | POS/Invoicing BRD v1.0 | SI lifecycle is primary fiscal document flow. |
| PSD-BRD-005 | Central PMS owns payment finality and ExitAuthorization. | Approved baseline | POS/Invoicing BRD v1.0 and v1.2 authority model | POS Server must not mark payment final or issue ExitAuthorization. |
| PSD-BRD-006 | POS Server owns fiscal issuance, counters, reports, EJ, POSLog, exports, audit, reprints, adjustments, retention, and recovery continuity. | Approved baseline | POS/Invoicing BRD v1.0 | Full design must define fiscal component architecture around these responsibilities. |
| PSD-BRD-007 | Fiscal issuance succeeds before Central PMS issues ExitAuthorization. | Approved baseline | POS/Invoicing BRD v1.0 | Issuance API/flow must return fiscal identity/status before Central PMS authorization. |
| PSD-BRD-008 | Reset counter starts at zero and increments only on fiscal reset. | Approved baseline | POS/Invoicing BRD v1.0 | Counter service must separate reset counter from Z-counter. |
| PSD-BRD-009 | Z-counter advances per Z-reading / fiscal day close. | Approved baseline | POS/Invoicing BRD v1.0 | X/Z service must handle approved fiscal scope once confirmed. |
| PSD-BRD-010 | Digital SI delivery is required where supported; POS Server returns digital SI URL. | Approved baseline | POS/Invoicing BRD v1.0 | Digital SI URL service and access controls are required design areas. |
| PSD-BRD-011 | QR code presentation is a channel/terminal presentation capability, not APM-only. | Approved baseline | POS/Invoicing BRD v1.0 | Channel presentation model must support optional QR rendering without shifting fiscal authority. |
| PSD-BRD-012 | Diplomat VAT Privilege / VAT Exemption is active VAT privilege/exemption, not ordinary discount. | Approved baseline | POS/Invoicing BRD v1.0 | Fiscal line and evidence design must include Diplomat treatment while details remain open. |

## Proposed Technical Defaults for Planning

These defaults are safe for planning but must be validated in the full System Design.

| ID | Proposed default | Rationale | Status |
| --- | --- | --- | --- |
| PSD-TD-001 | Model POS Server as a logical bounded component with fiscal services, not as a channel-owned subsystem. | Aligns Site-level POS Server decision. | Proposed default |
| PSD-TD-002 | Use a canonical fiscal record as the source for printed SI, digital SI, EJ, POSLog, reports, exports, and audit. | Prevents divergent fiscal facts. | Proposed default |
| PSD-TD-003 | Treat QR code generation/display as channel presentation using POS Server-returned URL. | Preserves fiscal authority at Site POS Server. | Proposed default |
| PSD-TD-004 | Make Sales Invoice and adjustment numbering formats configurable until BIR/accounting confirms final formats. | Numbering is open but design must prepare for configuration. | Proposed default |
| PSD-TD-005 | Default offline fiscal issuance to disabled/restricted until BIR/accounting approves a model. | Offline issuance creates high counter and sequence risk. | Proposed default |
| PSD-TD-006 | Use supervised recovery gate if continuity cannot be proven. | Required by BRD continuity requirement. | Proposed default |
| PSD-TD-007 | Separate fiscal record retention from entitlement/evidence retention. | BIR fiscal retention and privacy/evidence retention may differ. | Proposed default |
| PSD-TD-008 | Use idempotent SI issuance request handling to avoid duplicate fiscal documents on retries. | Supports exception/retry workflow. | Proposed default requiring detailed design |

## Pending Technical Decisions

| ID | Pending decision | Why unresolved | Blocks |
| --- | --- | --- | --- |
| PSD-PD-001 | Exact Sales Invoice and adjustment document numbering patterns. | Requires BIR/accounting confirmation. | API, database, implementation, sample outputs |
| PSD-PD-002 | Reset counter print/append behavior. | Requires BIR/accounting confirmation. | Print/report rendering |
| PSD-PD-003 | Fiscal identity assignment across Site POS Server and channels/terminals. | Requires BIR/accounting and accreditation guidance. | Terminal registry, headers/footers, accreditation package |
| PSD-PD-004 | WebPay fiscal terminal identity. | No physical printer or hardware serial. | WebPay fiscal issuance design |
| PSD-PD-005 | APM print/render responsibility. | Need to align Site POS Server authority with APM hardware behavior. | APM integration and certification |
| PSD-PD-006 | X-read and Z-read fiscal scope. | Site, terminal, cashier/session, or combined model remains open. | X/Z service, reports, cashier accountability |
| PSD-PD-007 | Offline fiscal issuance policy. | High-risk compliance decision. | Continuity, EC/APM/cashier outage design |
| PSD-PD-008 | VAT/tax treatment and fiscal line catalog. | Finance/accounting and BIR advisor must confirm. | Fiscal calculations and reports |
| PSD-PD-009 | Diplomat VAT Privilege / VAT Exemption evidence, wording, and reporting. | Active category, but details under RMO No. 10-2019 are open. | Fiscal line/evidence/report implementation |
| PSD-PD-010 | Digital SI URL access, expiry, authentication/access model, and audit treatment. | Requires security, privacy, compliance, and design confirmation. | Digital SI URL service and API contract |
| PSD-PD-011 | Tamper-evident fiscal state and external anchoring mechanism. | BRD requires continuity proof; mechanism is technical design. | Recovery and implementation |
| PSD-PD-012 | Export formats and POSLog/EJ reconciliation model. | Multiple source formats mentioned. | Exports, API, certification |

## Non-Decisions

| ID | Non-decision | Note |
| --- | --- | --- |
| PSD-ND-001 | No full System Design is drafted in this task. | This is a planning artifact set only. |
| PSD-ND-002 | No database schema is proposed. | Database impact is identified for future design only. |
| PSD-ND-003 | No API endpoint or DTO is proposed. | API impact is identified for future contract work only. |
| PSD-ND-004 | No source code change is proposed. | Documentation and diagrams only. |
| PSD-ND-005 | No DOCX output is generated. | Markdown and diagram assets only. |

