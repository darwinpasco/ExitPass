# ExitPass POS Server API Contract Decision Log

Status: Initial API contract planning artifact only

This decision log separates inherited approved decisions, safe API planning defaults, unresolved API decisions, and non-decisions. It does not define final endpoint paths, DTOs, database schema, or event payloads.

## 1. Approved BRD/System Design Decisions Inherited Into API Contract

| ID | Decision | Status | Source | API contract impact |
| --- | --- | --- | --- | --- |
| API-BRD-001 | POS/Invoicing is platform-wide, not APM-only. | Approved baseline | POS/Invoicing BRD v1.0 | API contract must support all applicable channels through Site POS Server. |
| API-BRD-002 | One Site-level POS Server serves one Site or parking operation boundary. | Approved baseline | POS/Invoicing BRD v1.0; POS Server System Design v1.0 | API calls must be scoped to or associated with the resolved Site POS Server. |
| API-BRD-003 | Channels/terminals are children of the Site POS Server. | Approved baseline | POS/Invoicing BRD v1.0 | Registry and presentation contracts must model child channel/terminal capability. |
| API-BRD-004 | Central PMS owns payment finality. | Approved baseline | BRD and System Design | POS Server API must consume finality context but not declare platform finality. |
| API-BRD-005 | Central PMS owns ExitAuthorization. | Approved baseline | BRD and System Design | POS Server API must not issue or expose ExitAuthorization operations. |
| API-BRD-006 | Fiscal issuance must succeed before Central PMS issues ExitAuthorization. | Approved baseline | BRD and System Design | Issuance API must return fiscal identity/status clearly enough for Central PMS to gate authorization. |
| API-BRD-007 | Sales Invoice is the primary parking fiscal output. | Approved baseline | BRD | Fiscal issuance family centers on Sales Invoice issuance. |
| API-BRD-008 | POS Server returns digital SI URL where digital delivery is enabled. | Approved baseline | BRD and System Design | Issuance/document/presentation contract must include digital SI URL semantics. |
| API-BRD-009 | Printed and digital SI must represent the same document and fiscal facts. | Approved baseline | BRD and System Design | Document lookup, URL, re-access, and reprint contracts must point to the same fiscal document. |
| API-BRD-010 | QR presentation is a channel/terminal capability, not APM-only. | Approved baseline | BRD and System Design | API must support QR presentation metadata without shifting fiscal authority. |
| API-BRD-011 | Offline fiscal issuance is disabled or restricted by default until approved. | Approved baseline | BRD and System Design | API contract must not imply offline fiscal issuance unless an approved model is later defined. |
| API-BRD-012 | POS Server owns fiscal reports, counters, EJ, POSLog, exports, adjustments, and recovery continuity. | Approved baseline | BRD and System Design | API route families must cover reports, exports, adjustments, reset/recovery, and status. |

## 2. Safe API Planning Defaults

| ID | Proposed default | Rationale | Status |
| --- | --- | --- | --- |
| API-TD-001 | POS Server issuance APIs must be idempotent. | Prevents duplicate fiscal documents on retries/timeouts. | Planning default; final semantics pending API Contract and BIR/accounting confirmation. |
| API-TD-002 | POS Server must return fiscal document identity/status to Central PMS. | Required before Central PMS records fiscal reference and issues ExitAuthorization. | Planning default. |
| API-TD-003 | POS Server must return digital SI URL where digital delivery is enabled. | Approved BRD requirement. | Planning default; access model remains open. |
| API-TD-004 | POS Server API must not expose ExitAuthorization issuance. | Preserves Central PMS authority. | Planning default. |
| API-TD-005 | API route families should be separated by fiscal issuance, documents, presentation, registry, identity, reports, exports, adjustments, reset/recovery, exception/status, and audit/event impact. | Keeps responsibilities reviewable and avoids channel-specific fiscal fragmentation. | Planning default; final naming open. |
| API-TD-006 | Central PMS is the primary fiscal issuance caller for payment-linked SI issuance. | Preserves payment finality and fiscal issuance choreography. | Planning default. |
| API-TD-007 | Channels/terminals receive presentation data but do not become fiscal issuer. | Preserves Site POS Server fiscal authority. | Planning default. |
| API-TD-008 | Offline fiscal issuance remains disabled/restricted by default. | Prevents unapproved counter/sequence continuity risk. | Planning default. |
| API-TD-009 | API contract should separate public/customer digital SI access from internal fiscal APIs. | Reduces security/privacy risk. | Planning default; final access model open. |

## 3. Unresolved API Decisions

| ID | Decision needed | Decision owner | Why it remains open |
| --- | --- | --- | --- |
| API-PD-001 | Final endpoint route family naming. | Architecture/API owners | Naming should follow v1.3 API contract conventions. |
| API-PD-002 | Request/response DTO boundaries and shared metadata. | Architecture/API owners | Needs full API contract draft, not planning artifact. |
| API-PD-003 | Idempotency key scope and duplicate request behavior. | Architecture, BIR/accounting, Central PMS owners | Must prevent duplicates and account for sequence-gap treatment. |
| API-PD-004 | Sequence gaps, reserved numbers, failed issuance, and abandoned issuance semantics. | BIR/accounting, architecture | BIR treatment is not yet confirmed. |
| API-PD-005 | Digital SI URL token/access/expiry/authentication/audit model. | Security/privacy, architecture | Customer access model requires security/privacy confirmation. |
| API-PD-006 | QR presentation payload and rendering responsibility. | Architecture, channel owners | System Design keeps QR rendering as channel/implementation concern. |
| API-PD-007 | WebPay fiscal terminal identity. | BIR/accounting, architecture | No physical printer/hardware serial. |
| API-PD-008 | APM printing model. | BIR/accounting, Hikvision/APM vendor, architecture | Must align Site POS Server fiscal authority with APM hardware behavior. |
| API-PD-009 | X-read and Z-read scope. | BIR/accounting, finance, operations | Scope may be Site, terminal, cashier/session, or combined. |
| API-PD-010 | Report/export formats. | BIR/accounting, compliance | Mandatory formats not yet final. |
| API-PD-011 | Fiscal adjustment workflow sequencing with Central PMS/provider. | Payments, finance, compliance, architecture | Payment refund/reversal finality and fiscal adjustment document sequencing must be coordinated. |
| API-PD-012 | Recovery continuity API and supervised recovery semantics. | Architecture, security, operations, compliance | Must align with database/recovery design. |
| API-PD-013 | Audit/event publication contracts. | Architecture, Engineering Pack, security | Final event model and outbox ownership remain open. |
| API-PD-014 | Security/RBAC model. | Security/privacy, compliance, operations | Final permission matrix and approval workflow remain open. |

## 4. Non-Decisions

| ID | Non-decision | Rationale |
| --- | --- | --- |
| API-ND-001 | This planning artifact does not define endpoint paths. | Endpoint paths belong to the full API Contract. |
| API-ND-002 | This planning artifact does not define DTO fields or schemas. | DTO shape belongs to the full API Contract. |
| API-ND-003 | This planning artifact does not define database tables, columns, or migrations. | Database design belongs to POS Server Database Design. |
| API-ND-004 | This planning artifact does not approve offline fiscal issuance. | Offline issuance requires BIR/accounting approval. |
| API-ND-005 | This planning artifact does not decide MIN/PTU/serial assignment. | Fiscal identity assignment requires BIR/accounting/accreditation confirmation. |
| API-ND-006 | This planning artifact does not decide digital SI public access security model. | Security/privacy review is required. |
| API-ND-007 | This planning artifact does not define final event schemas. | Event schemas belong to API Contract Pack/Engineering Pack. |
| API-ND-008 | This planning artifact does not modify approved BRD or System Design decisions. | API Contract must implement, not reinterpret, the approved baselines. |
