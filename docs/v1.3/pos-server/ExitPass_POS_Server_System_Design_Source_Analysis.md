# ExitPass POS Server System Design Source Analysis

Version: v1.0 planning artifact
Status: Draft for system design planning only
Generated: 2026-06-25

## Purpose

This artifact identifies source-driven technical design inputs for the future `ExitPass POS Server System Design v1.0`. It translates the approved POS/Invoicing BRD baseline into design areas without drafting the full system design and without creating database, API, or implementation changes.

Authoritative BRD baseline: `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`.

## Source Baseline

| Source | Use in POS Server System Design |
| --- | --- |
| Approved POS/Invoicing BRD v1.0 | Primary business requirements and authority model. |
| POS/Invoicing decision log and recommendations | Accepted decisions, proposed defaults, and unresolved blockers for design. |
| POS/Invoicing open questions | Open compliance, accounting, security, privacy, and implementation questions to preserve. |
| POS Server impact map | Downstream impact expectations for Central PMS, channels, reports, audit, events, database, and API packs. |
| ExitPass v1.2 BRD/System Design/Database/API/Engineering Pack | Existing authority boundaries, payment finality, ExitAuthorization, site/site-group, parking session, provider outcome, and platform integration baseline. |
| RMO No. 24-2023 and Annex D/E/F/G references | Fiscal document, X/Z, sales summary, statutory sales book, evaluation checklist, and meeting guidance inputs. |
| Hikvision APM gap analysis and developer checklist | APM printing, POSLog/EJ/export, BIR accreditation gaps, and hardware/terminal constraints. |
| BIR RMO No. 10-2019 | Diplomat VAT Privilege / VAT Exemption design input and evidence/reporting open questions. |

## Design Findings by Area

### Component Boundary

| Source-driven input | Design implication |
| --- | --- |
| BRD approves platform-wide POS/Invoicing, not APM-only. | POS Server must be designed as a Site-level fiscal component that serves all applicable channels. |
| BRD approves one Site-level POS Server per Site or parking operation boundary. | POS Server boundary follows resolved Site / operation boundary, not payment channel. |
| Channels/terminals are children of the Site POS Server. | WebPay, APM, Cashier POS, EC/continuity, operator-assisted, and future channels need registration/identity under the Site POS Server. |

### Authority Model

| Authority | Owner | Design implication |
| --- | --- | --- |
| Parking session control state | Central PMS | POS Server consumes parking/payment context; it does not become session authority. |
| Site resolution | Central PMS | POS Server request must carry or be tied to the resolved Site. |
| Payment finality | Central PMS | POS Server must issue fiscal documents only after Central PMS verified finality, except any approved offline policy remains open. |
| ExitAuthorization | Central PMS | POS Server must never issue ExitAuthorization. |
| Fiscal issuance and reports | Site POS Server | POS Server owns SI issuance, counters, reports, EJ, POSLog, audit, reprints, adjustments, exports, retention, and fiscal recovery continuity. |

### Fiscal Document Lifecycle

| Source-driven input | Design implication |
| --- | --- |
| Parking fiscal output is Sales Invoice. | Sales Invoice lifecycle is the primary fiscal document lifecycle for v1.3 parking payments. |
| Fiscal issuance before ExitAuthorization is decided. | Issuance flow must return fiscal identity/status before Central PMS authorizes exit. |
| Adjustment documents remain design considerations. | Void/refund/cancel/return document sequencing and numbering remain open design items linked to Central PMS refund/reversal authority. |
| Sequence gaps and numbering formats remain open. | Numbering service design must support confirmed formats and idempotent issuance without silently finalizing sequence policy. |

### Printed and Digital SI Delivery

| Source-driven input | Design implication |
| --- | --- |
| Sales Invoice supports printed and digital presentation. | Renderer and delivery model must produce consistent printed and digital representations of the same issued SI. |
| POS Server returns a digital SI URL. | System Design must define URL generation, access, expiry, authentication/access model, audit, and retention after security/privacy confirmation. |
| QR code presentation is channel/terminal presentation capability, not APM-only. | APM, Cashier POS, EC/continuity, operator-assisted, and future channels can present QR if supported; fiscal authority remains Site POS Server. |
| Printed and digital forms must not diverge. | Canonical fiscal record must be the source for print, digital view, EJ, POSLog, reports, exports, and audit. |

### Digital SI URL and QR Code Presentation

| Source-driven input | Design implication |
| --- | --- |
| URL must not allow unauthorized modification. | Digital SI view must be immutable from customer access perspective. |
| URL must not expose unnecessary sensitive data. | Access model must minimize data exposure and consider token/authentication/privacy controls. |
| Expiry and authentication model remain open. | System Design must present options and identify required security/privacy decisions. |
| QR presentation does not create fiscal authority. | Terminal QR rendering is a delivery/presentation function only. |

### Counters and Fiscal State

| Source-driven input | Design implication |
| --- | --- |
| Reset counter starts from zero and increments only on fiscal reset. | Counter service must separate reset events from Z-close events. |
| Z-counter advances per Z-reading / fiscal day close. | X/Z reporting design must define scope after BIR/accounting confirmation. |
| Grand Total Amount must be preserved. | Fiscal state must persist accumulator history and reset snapshots. |
| Restore must not resume from lower counters or stale fiscal state. | Recovery design needs tamper-evident state, continuity checks, supervised recovery gate, and recovery audit record. |

### Reports and Exports

| Source-driven input | Design implication |
| --- | --- |
| BIR Sales Summary is first-class, not analytics. | Reporting service must reconcile to SI sequence, counters, VAT/deductions, GTA, EJ/POSLog, and audit. |
| Annex E sales books are required structures. | Reporting model must support Senior, PWD, NAAC, Solo Parent, and Diplomat VAT treatment. |
| EJ, POSLog, JSON/PDF/TXT/ARTS options remain open. | Export service must support configurable/confirmed formats and preserve canonical reconciliation. |

### Channel/Terminal Model

| Channel | Source-driven design input |
| --- | --- |
| WebPay | Fiscal terminal identity is open because there is no physical printer or hardware serial. |
| APM | APM print model is open; POS Server authority must be preserved. |
| Cashier POS | Needs cashier/session accountability, reprint/adjustment controls, and optional QR presentation. |
| EC Device / Continuity Terminal | Offline fiscal issuance remains restricted until confirmed. |
| Operator-assisted payment | If allowed, must route through Site POS Server and preserve operator identity/context. |
| Future channels | Must register as child channels/terminals and not become independent POS systems. |

### Security/RBAC

| Source-driven input | Design implication |
| --- | --- |
| High-risk fiscal actions require authorization and audit. | RBAC must cover Z-close, reset, reprint, void/refund/cancel/return, export, configuration, restore, recovery, and manual release support. |
| Role separation is required. | Cashier, supervisor, fiscal admin, compliance auditor, recovery/DR approver, and system admin duties must be separated. |

### Privacy/Evidence

| Source-driven input | Design implication |
| --- | --- |
| Entitlement and Diplomat evidence may include sensitive data. | Evidence storage/reference model must minimize data and honor retention differences. |
| Digital SI URL access exposes customer-facing fiscal data. | Access policy, expiry, authentication/access model, logging, and privacy controls need design and confirmation. |

### Recovery/DR

| Source-driven input | Design implication |
| --- | --- |
| Restore/failover must prove continuity. | System Design must define continuity proof using last GTA, reset counter, Z-counter, SI sequence, latest EJ hash, and last fiscal event timestamp. |
| Unproven continuity blocks issuance. | Design needs supervised recovery workflow and recovery audit event before fiscal issuance resumes. |

### Integration Impact

| Integration | Design implication |
| --- | --- |
| Central PMS | Requires SI issuance request/response, fiscal reference recording, exception/retry coordination, and no authorization bypass. |
| Payment Orchestrator | Reports provider outcome to Central PMS only; it does not call POS Server as payment finality authority. |
| WebPay and channels | Consume fiscal status, printed/digital SI presentation data, digital SI URL, and optional QR payload. |
| Vendor PMS / HikCentral | Receives synchronization only; not fiscal or authorization authority. |
| Operator Console | Likely impact for fiscal actions, evidence references, RBAC, reports, exceptions, and supervisor approvals. |

### Database/API Impact

| Area | Planning implication |
| --- | --- |
| Database Design v1.3 | Needs later schema proposal for fiscal documents, fiscal lines, counters, terminal registry, EJ, POSLog, audit, exports, URL access, recovery state, and retention. |
| API Contract Pack v1.3 | Needs later contracts for issuance, status, digital SI URL, channel presentation, reports, reprint, adjustment, export, terminal registration, and recovery. |
| Eventing/outbox | Needs later design for fiscal issuance events, report/export events, retry, audit, recovery, and reconciliation. |

### Open Questions

Open items must remain visible in the future System Design. Most blocking areas are fiscal identity assignment, numbering, X/Z scope, offline issuance, adjustment sequencing, tax treatment, Diplomat treatment/evidence, export formats, supplier/applicant responsibility, recovery continuity mechanism, and digital SI URL access policy.

