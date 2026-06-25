# ExitPass POS Server API Contract v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass POS Server API Contract |
| Version | v1.0 Markdown draft |
| Product scope | ExitPass v1.3 POS Server API |
| Status | Draft for review |
| Generated | 2026-06-25 |
| Output format | Markdown only |
| BRD baseline | `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` |
| System Design baseline | `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` |

## 2. Purpose and Scope

This document defines the first API contract draft for the ExitPass Site-level POS Server. It translates the approved POS/Invoicing BRD and approved POS Server System Design into API ownership, route families, request/response semantics, status behavior, idempotency behavior, authentication/authorization expectations, error model, and integration responsibilities.

This contract covers API planning and contract semantics for:

- Central PMS to POS Server fiscal issuance.
- Sales Invoice issuance, status, lookup, print/digital consistency, and digital SI URL return.
- Digital SI URL access and channel/terminal QR presentation from the POS Server-returned URL.
- Channel and terminal registration/status.
- Fiscal identity configuration.
- Reprints and repeated digital access.
- Void/refund/cancel/return fiscal adjustments.
- X-read, Z-read, BIR Sales Summary, Annex E, EJ, POSLog, and fiscal exports.
- Fiscal reset, recovery continuity check, supervised recovery, exception/retry status, and audit/event impact.
- WebPay, APM, Cashier POS, EC Device / Continuity Terminal, operator-assisted payment, and future channel integration responsibilities.

This document does not define final database tables, columns, indexes, constraints, migrations, final DTO schemas, final event schemas, final status-code enum storage, or implementation internals.

Route names and endpoint candidates in this document are provisional until API contract review.

## 3. Reference Baseline

| Reference | Role in this contract |
| --- | --- |
| `ExitPass_POS_Invoicing_BRD_v1.0.md` | Approved business requirements and authority model. |
| `ExitPass_POS_Server_System_Design_v1.0.md` | Approved technical design and API impact baseline. |
| POS Server API Contract planning artifacts | Source analysis, decision log, outline, open questions, and impact map. |
| POS Server System Design technical and approval-readiness reviews | Confirms design readiness and non-blocking cleanup closure. |
| ExitPass API Contract Pack v1.2 | Supporting baseline for platform authority and integration conventions. |
| ExitPass BRD/System Design/Database/Engineering Pack v1.2 | Supporting baseline for Central PMS, payment, session, site, vendor, and ExitAuthorization authority. |
| RMO No. 24-2023 and Annex D/E/F/G references | Supporting fiscal reporting, X/Z, BIR Sales Summary, sales books, evaluation, and accreditation inputs. |
| Hikvision APM gap analysis and developer checklist | Supporting APM print/presentation, POSLog/EJ/export, and terminal behavior inputs. |
| BIR RMO No. 10-2019 | Supporting Diplomat VAT Privilege / VAT Exemption inputs. |

## 4. API Ownership and Authority Model

The API contract shall preserve the approved authority model.

| Authority area | Owner | API rule |
| --- | --- | --- |
| Payment finality | Central PMS | POS Server APIs consume verified payment finality context from Central PMS but do not declare platform payment finality. |
| ExitAuthorization | Central PMS | POS Server APIs must not issue, approve, create, mutate, or bypass ExitAuthorization. |
| Fiscal issuance | Site POS Server | POS Server owns Sales Invoice issuance and fiscal document lifecycle for the resolved Site. |
| Fiscal document numbering | Site POS Server | POS Server owns SI and adjustment numbering according to confirmed policy. |
| Digital SI URL | Site POS Server | POS Server returns digital SI URL where digital delivery is enabled. |
| Channel/terminal presentation | Channel/terminal under Site POS Server | Channels/terminals may display or print SI, present the digital SI URL, or convert the POS Server-returned URL into a QR code where supported without becoming fiscal issuer. |
| Refund/reversal money movement | Central PMS/payment provider | POS Server owns related fiscal adjustment documents, not money movement finality. |
| Gate/exit execution | Central PMS authorization chain | Gate/exit execution must not bypass Central PMS authorization. |

Payment Orchestrator and WebPay must not declare platform payment finality. Vendor PMS / HikCentral acknowledgment remains synchronization only and is not payment finality or exit authority.

## 5. API Consumers and Trust Boundaries

| Consumer / boundary | Contract posture |
| --- | --- |
| Central PMS | Primary trusted internal caller for payment-linked SI issuance, fiscal reference recording, exception status, and controlled fiscal workflow coordination. |
| WebPay | Channel-facing consumer of digital SI presentation/status through approved Central PMS/POS Server flow; not finality authority. |
| APM | Terminal/channel that may present or print POS Server-issued SI and generate/display/print QR from the digital SI URL where supported; not fiscal authority. |
| Cashier POS | Terminal/channel that may support SI presentation, reprint, adjustment, and cashier/session context under authorization. |
| EC Device / Continuity Terminal | Continuity terminal/channel under Site POS Server authority; offline fiscal issuance remains restricted unless approved. |
| Operator-assisted terminal/workflow | Assisted payment flow that may present SI, digital SI URL, channel-side QR, and exception status; operator cannot declare platform finality. |
| Future channels | Must register as child channels/terminals and follow the same fiscal authority pattern. |
| Fiscal administrators | Privileged internal actors for fiscal identity, export, reset, recovery, and configuration workflows. |
| Compliance auditors | Read/export/audit consumers according to RBAC and retention policy. |
| Customer digital SI URL access | Separate public/customer access boundary, open for Security/Privacy Review and final API design. |

Internal APIs and public/customer digital SI access must be treated as separate trust boundaries.

## 6. Common Contract Rules

All route families shall follow these common contract rules unless a later approved exception is documented.

### Common Headers and Metadata

| Semantic item | Required use |
| --- | --- |
| `X-Correlation-Id` | Required for traceability across Central PMS, POS Server, channel/terminal, and audit flows. |
| `Idempotency-Key` | Required for side-effecting operations, including issuance, reprint, adjustment, report/export generation, reset, recovery, and exception closure where applicable. |
| Site context / resolved Site POS Server context | Required for fiscal operations that depend on Site-level fiscal authority. |
| Service identity / actor identity | Required to distinguish system callers, users, operators, supervisors, fiscal admins, auditors, and recovery approvers. |
| Channel/terminal identity | Required where fiscal issuance, presentation, cashier/session accountability, print, display, QR, or terminal status is involved. |
| Request timestamp | Required for audit and replay analysis. Final clock authority and rollback controls remain open. |
| Audit reference | Required in responses where a fiscal or privileged action is accepted, completed, rejected, or blocked. |

Exact field names beyond `X-Correlation-Id` and `Idempotency-Key` remain subject to API Contract review.

### Common Behavioral Rules

- POS Server APIs shall not issue ExitAuthorization.
- POS Server APIs shall not declare platform payment finality.
- Side-effecting operations shall be idempotent.
- Responses shall distinguish accepted, completed, failed, blocked, pending, duplicate/idempotent replay, and unauthorized outcomes at semantic level.
- All fiscal outputs shall reconcile to canonical fiscal records.
- Printed and digital SI output shall represent the same fiscal document and fiscal facts.
- Fiscal identity, numbering, export, X/Z scope, offline issuance, digital SI URL access, and security/RBAC open questions shall remain visible.

## 7. Authentication and Authorization

The final authentication and authorization model remains open for Security/Privacy Review and API Contract review. The contract shall support these control requirements:

- Internal service authentication for Central PMS, POS Server, and approved channel/terminal systems.
- Separate access treatment for public/customer digital SI URL access.
- RBAC for privileged fiscal operations.
- Strong authorization for high-risk fiscal actions:
  - Reprints.
  - Void/refund/cancel/return fiscal adjustments.
  - Fiscal identity configuration changes.
  - X/Z close operations where required.
  - Fiscal reset.
  - Recovery continuity override or supervised recovery.
  - Fiscal exports and compliance access.
- Audit capture for privileged actions, approval decisions, denial outcomes, and configuration changes.

Role concepts to support at contract level:

- Cashier.
- Supervisor.
- Fiscal administrator.
- Compliance auditor.
- Recovery/DR approver.
- System administrator.
- Service caller.
- Channel/terminal service identity.

Final permission matrix, auth mechanism, token format, claims model, and policy enforcement details remain open.

## 8. Idempotency and Retry Model

Idempotency is required to prevent duplicate fiscal documents during retries, timeouts, and network failures.

### Required Semantics

- Side-effecting requests shall include an `Idempotency-Key`.
- POS Server shall detect duplicate requests for the same fiscal operation according to the final idempotency key scope.
- A successful idempotent replay shall return the same fiscal document identity/status where safe and applicable.
- An idempotency conflict shall be distinguishable from a retry of the same request.
- Timeout handling shall allow Central PMS to query issuance status before retrying or authorizing exit.
- Retry behavior shall not create duplicate fiscal documents.
- Sequence gaps, reserved numbers, failed issuance, and abandoned issuance handling remain open for BIR/accounting confirmation and API Contract review.

### Open Idempotency Decisions

- Final idempotency key scope.
- Duplicate issuance response behavior.
- Handling of request payload mismatch under same idempotency key.
- Sequence reservation timing.
- Failed issuance and abandoned issuance representation.
- Retention period for idempotency records.
- Relationship to database design and event/outbox replay.

## 9. Canonical Error Model

The API shall use a canonical error envelope at semantic level. Final DTO fields and exact status codes remain pending API Contract review.

The error response should support:

- Business error code.
- Human-readable message safe for operator/system use.
- Retryable flag.
- Correlation ID.
- Audit reference where applicable.
- Fiscal document reference where applicable.
- Channel/terminal reference where applicable.
- Recovery or exception reference where applicable.

Proposed contract error codes, pending review:

| Proposed error code | Intended meaning |
| --- | --- |
| `POS_SERVER_UNAVAILABLE` | POS Server cannot be reached or cannot process the request. |
| `FISCAL_ISSUANCE_FAILED` | Sales Invoice issuance failed. |
| `FISCAL_ISSUANCE_TIMEOUT` | Issuance request timed out or completion is unknown. |
| `FISCAL_DOCUMENT_ALREADY_ISSUED` | Fiscal document already exists for the idempotent issuance intent. |
| `IDEMPOTENCY_CONFLICT` | Same idempotency key conflicts with different request semantics. |
| `UNAUTHORIZED_CALLER` | Internal service, channel, terminal, or user identity is not authorized to call the API. |
| `FISCAL_ACTION_NOT_AUTHORIZED` | User, role, service, or approval context is not authorized for the requested fiscal action. |
| `INVALID_SITE_POS_SERVER` | Request targets or resolves to an invalid Site POS Server. |
| `FISCAL_IDENTITY_NOT_CONFIGURED` | Required fiscal identity is missing or inactive. |
| `NUMBERING_POLICY_NOT_CONFIGURED` | Required fiscal numbering policy is missing or inactive. |
| `DIGITAL_SI_URL_UNAVAILABLE` | Digital SI URL cannot be generated or retrieved. |
| `DIGITAL_SI_ACCESS_DENIED` | Digital SI access request is denied. |
| `REPORT_GENERATION_FAILED` | Report generation failed. |
| `EXPORT_GENERATION_FAILED` | Export generation failed. |
| `EXPORT_VALIDATION_FAILED` | Structured fiscal export validation failed against an approved schema or validation profile. |
| `RESET_REQUIRES_APPROVAL` | Fiscal reset request requires approval. |
| `RECOVERY_CONTINUITY_FAILED` | Recovery continuity check failed. |
| `OFFLINE_FISCAL_ISSUANCE_NOT_ALLOWED` | Offline fiscal issuance is not approved for the requested operation. |

## 10. Fiscal Issuance API Family

Provisional route family: `/v1/pos/fiscal-issuance/*`

This family covers Sales Invoice issuance requested by Central PMS after verified payment finality.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Issue Sales Invoice | Central PMS requests SI issuance for a resolved Site after verified payment finality. | Provisional |
| Get issuance status | Central PMS checks the state of an issuance request after timeout, retry, or pending exception. | Provisional |
| Retry issuance | Central PMS or controlled retry workflow requests retry without duplicate fiscal document creation. | Provisional |

### Request Semantics

The issuance request shall include or reference:

- Resolved Site POS Server context.
- Central PMS payment finality reference.
- Parking session reference.
- Payment confirmation reference.
- Channel/terminal identity where applicable.
- Customer/buyer context where applicable and approved.
- Fiscal lines or fiscal line basis.
- Entitlement/VAT privilege context where applicable.
- Presentation preference/capability context for printed/digital/QR output.
- `X-Correlation-Id`.
- `Idempotency-Key`.
- Service identity and actor identity where applicable.

Exact DTO fields remain pending API Contract review.

### Validation Semantics

Before issuing a Sales Invoice, POS Server shall validate:

- Resolved Site and Site POS Server match.
- Request is scoped to the correct Site POS Server.
- Central PMS payment finality context is present and acceptable.
- Channel/terminal registration is valid where applicable.
- Channel/terminal is active or allowed for the requested operating mode.
- Fiscal identity is configured and active.
- Numbering policy is configured and available.
- Fiscal line basis is present and eligible.
- Entitlement/VAT privilege context is acceptable where applicable.
- Digital SI delivery configuration is valid where digital delivery is requested.
- No recovery, reset, fiscal lock, or continuity block prevents issuance.

If validation fails:

- POS Server shall return a blocked or failed semantic response.
- Response shall identify the validation area at business/error-code level.
- POS Server shall not issue the Sales Invoice.
- Central PMS shall not issue ExitAuthorization on a failed or blocked fiscal issuance response.

Final DTO fields for validation failures remain pending API Contract review.

### Response Semantics

Successful issuance response shall return:

- Fiscal document identity.
- Fiscal document type: Sales Invoice.
- Issuance status.
- Sales Invoice number or confirmed fiscal identity reference according to final numbering policy.
- Site POS Server identity.
- Digital SI URL where digital delivery is enabled.
- Presentation metadata where applicable.
- Audit reference.
- Correlation ID.

Failure or pending response shall return:

- Status indicating failed, retry pending, pending recovery, blocked, or unknown/timeout state.
- Business error code where applicable.
- Retryable flag.
- Exception or recovery reference where applicable.
- Audit reference where applicable.

### Authority Rules

- POS Server shall not issue ExitAuthorization.
- Central PMS records the returned fiscal reference and then issues ExitAuthorization.
- If fiscal issuance fails or times out, Central PMS shall not issue ExitAuthorization until controlled handling is completed.

## 11. Fiscal Document API Family

Provisional route family: `/v1/pos/fiscal-documents/*`

This family covers fiscal document lookup, status, and document-level references.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Get fiscal document status | Retrieve fiscal document state and references. | Provisional |
| Get Sales Invoice presentation references | Retrieve print/digital presentation references for the same issued SI. | Provisional |
| Get original document linkage | Retrieve original document references for reprint or adjustment workflows. | Provisional |

### Contract Semantics

The Fiscal Document API shall support:

- Sales Invoice identity/status.
- Printed/digital consistency.
- Original fiscal document reference.
- Adjustment document linkage.
- Reprint status and audit visibility.
- Digital SI URL status where applicable.
- Canonical fiscal document state for reconciliation.

The API shall not allow fiscal document mutation except through approved side-effecting families such as reprint, adjustment, reset/recovery, or fiscal identity configuration where applicable.

## 12. Digital SI URL and Presentation API Family

Provisional route family: `/v1/pos/digital-si/*`

This family covers digital SI URL retrieval, access status, and channel/terminal receipt of the POS Server-returned digital SI URL.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Get digital SI URL | Retrieve or return the POS Server-issued digital SI URL for an issued SI. | Provisional |
| Get digital SI access status | Determine whether URL is active, expired, revoked, or blocked. | Provisional |
| Get digital SI presentation status | Provide status needed by trusted services/channels to present the issued digital SI URL according to approved policy. | Provisional |
| Register digital SI re-access | Record repeated digital access where audit policy requires it. | Provisional |

### Contract Semantics

- POS Server returns digital SI URL where digital delivery is enabled.
- POS Server returns only the digital Sales Invoice URL for QR presentation.
- POS Server does not generate the QR image as a required API responsibility.
- Digital SI URL points to the same issued SI as the printed SI.
- Digital SI URL shall not allow unauthorized modification of the SI.
- Digital SI URL shall not expose unnecessary sensitive data.
- URL access policy, expiry policy, authentication/access model, and audit treatment remain open for Security/Privacy Review and API Contract review.
- Customer-facing digital SI URL access is a separate trust boundary from internal POS Server digital SI and presentation APIs.
- Internal APIs may return the digital SI URL and digital SI status to trusted services, channels, or terminals.
- Customer-facing access must follow the approved public/customer access model, expiry policy, authentication/access policy, privacy rules, and audit treatment.
- Channels/terminals may receive the digital SI URL.
- QR presentation is a channel/terminal display or print capability.
- QR presentation is not APM-only.
- QR presentation does not make the terminal/channel the fiscal issuer.
- The channel or terminal converts the POS Server-returned URL into a QR code where QR presentation is supported.
- QR generation, display, and printing are channel/terminal presentation responsibilities.
- Site POS Server remains the fiscal issuer.
- APM, Cashier POS, EC Device / Continuity Terminal, operator-assisted terminals, and future channels may support QR presentation where approved.

Channel/terminal QR image rendering implementation details are channel/terminal implementation concerns. The API Contract shall not create a POS Server QR generation API as a required responsibility.

## 13. Channel and Terminal Registry API Family

Provisional route family: `/v1/pos/channels/*`

This family covers channel/terminal registration, status, capability, and audit.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Register channel/terminal | Register a channel or terminal under a Site POS Server. | Provisional |
| Update channel/terminal status | Set active, inactive, degraded, continuity, ONLINE/OFFLINE, or equivalent reachability/health state. | Provisional |
| Get channel/terminal status | Retrieve identity, Site association, capabilities, and state. | Provisional |
| List channels/terminals for Site POS Server | Support operational lookup and configuration review. | Provisional |

### Contract Semantics

Registry contract shall support:

- Resolved Site association.
- Channel/terminal type.
- Channel/terminal identity.
- Presentation capability flags:
  - Print support.
  - Display support.
  - Digital SI URL support.
  - QR presentation support.
- Fiscal identity reference where applicable.
- Cashier/session support where applicable.
- Active/inactive/degraded/continuity state.
- ONLINE/OFFLINE or equivalent reachability/health state where applicable.
- POS Server administrative/status APIs should support ONLINE/OFFLINE status where required.
- Channel/terminal status APIs should support ONLINE/OFFLINE or equivalent reachability/health state where applicable.
- ONLINE/OFFLINE is operational and observability information.
- ONLINE/OFFLINE does not approve offline fiscal issuance.
- Offline fiscal issuance remains disabled/restricted unless BIR/accounting approves a compliant sequence, counter, evidence, reconciliation, and recovery model.
- Audit of changes.

Final registry field model remains open for POS Server API Contract and Database Design.

## 14. Fiscal Identity Configuration API Family

Provisional route family: `/v1/pos/fiscal-identity/*`

This family covers fiscal identity configuration and status. It must be privileged and auditable.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Configure fiscal identity | Configure taxpayer/Site/branch/POS Server/channel/terminal fiscal identity metadata. | Provisional |
| Get fiscal identity status | Retrieve active/inactive/missing fiscal identity state. | Provisional |
| Validate fiscal identity readiness | Determine whether issuance/reporting can proceed for a Site POS Server or terminal/channel. | Provisional |

### Contract Semantics

Fiscal identity contract shall support:

- Taxpayer / registered business name.
- Registered address.
- TIN and VAT/non-VAT classification.
- Site or branch/location identity.
- Site POS Server fiscal identity.
- Terminal/channel identity where applicable.
- MIN.
- PTU or ATG details if applicable.
- Serial number.
- Terminal number.
- Software name and version.
- Supplier accreditation metadata.
- Required BIR footer text.
- Required non-input-tax warning where applicable.
- Status and effective dates.

The contract shall not decide final MIN/PTU/serial/software/supplier assignment between Site POS Server and channels/terminals. That remains open for BIR/accounting and accreditation confirmation.

## 15. Reprint API Family

Provisional route family: `/v1/pos/reprints/*`

This family covers controlled reprints for Sales Invoice, X-read, Z-read, and Electronic Journal outputs where applicable.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Request reprint | Request a controlled reprint for an issued fiscal document or report output. | Provisional |
| Get reprint status | Retrieve reprint request status and audit reference. | Provisional |
| Get reprint history | Retrieve authorized reprint history for a document. | Provisional |

### Contract Semantics

Reprint contract shall support:

- Original document, report, or fiscal output linkage.
- Reprint type, including Sales Invoice, X-read, Z-read, or Electronic Journal where applicable.
- Reprint reason.
- Requesting actor/service identity.
- Authorization and approval where required.
- Reprint timestamp.
- Reprint status/history.
- Reprint label/audit behavior.
- Audit reference.
- No mutation of original fiscal document facts.
- No mutation of original report or Electronic Journal facts.
- Relationship to repeated digital access where required.

Where BIR requires it, reprinted fiscal outputs shall show `REPRINT` and `DATE / TIME REPRINTED` at the bottom of the reprinted output. POS Server shall preserve or return enough reprint metadata/status for the renderer, channel, or terminal to apply required labels and timestamps. Exact output layout and repeated digital access audit rules remain open for BIR/accounting and security/privacy confirmation.

## 16. Fiscal Adjustment API Family

Provisional route family: `/v1/pos/adjustments/*`

This family covers void, refund, cancel, return, and related fiscal adjustment document workflows.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Request fiscal adjustment | Request void/refund/cancel/return fiscal adjustment linked to an original document. | Provisional |
| Get adjustment status | Retrieve adjustment request/document status. | Provisional |
| Link payment reversal context | Associate Central PMS/provider refund or reversal context where applicable. | Provisional |

### Contract Semantics

Fiscal adjustment contract shall support:

- Adjustment type concept: void, refund, cancel, return, or other confirmed fiscal adjustment.
- Original fiscal document linkage.
- Adjustment document identity/status.
- Reason code.
- Requesting actor/service identity.
- Approval where required.
- Payment refund/reversal reference where applicable.
- Reconciliation reference.
- Audit reference.

POS Server owns fiscal adjustment documents. Central PMS/payment provider owns payment refund/reversal finality.

Workflow sequencing remains open for POS Server API Contract, finance/compliance, and payment architecture confirmation.

## 17. X-read and Z-read API Family

Provisional route family: `/v1/pos/reports/xz/*`

This family covers X-read and Z-read generation, status, and export semantics.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Request X-read | Generate X-read for approved scope. | Provisional |
| Request Z-read | Generate Z-read and close applicable fiscal day/scope. | Provisional |
| Get X/Z status | Retrieve report generation state and references. | Provisional |
| Export X/Z report | Retrieve report output in confirmed format. | Provisional |

### Contract Semantics

- X-read shall be producible for BIR/accounting-approved operational scopes.
- Potential scopes include Site POS Server, terminal/channel, cashier/session, or combined scope.
- Z-read shall close the applicable fiscal day for approved fiscal scope.
- Z-read advances Z-counter.
- Z-read does not advance reset counter.
- X/Z reports shall reconcile to canonical fiscal records, SI sequence, counters, GTA, EJ, POSLog, BIR Sales Summary, and audit records as applicable.

Final X/Z scope and aggregation model remains open for BIR/accounting confirmation.

## 18. BIR Sales Summary and Annex E Report API Family

Provisional route family: `/v1/pos/reports/bir/*`

This family covers BIR Sales Summary and Annex E report request/status/export.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Request BIR Sales Summary | Generate BIR Sales Summary for approved period/scope. | Provisional |
| Request Annex E report | Generate Annex E-1 to E-5 report structures where applicable. | Provisional |
| Get report status | Retrieve report generation status. | Provisional |
| Export report | Retrieve report output in confirmed format. | Provisional |

### Contract Semantics

Report contract shall support:

- BIR Sales Summary as a first-class required report.
- Annex E-1 to E-5 support.
- Senior Citizen and PWD immediate workflows.
- NAAC and Solo Parent future-supported categories.
- Diplomat VAT Privilege / VAT Exemption as active VAT privilege/exemption category.
- VATable, VAT-exempt, zero-rated, non-VAT, statutory discount, VAT privilege/exemption, coupon, penalty, lost ticket, overstay, service charge, and adjustment classifications where applicable.
- Reconciliation to canonical fiscal records.
- BIR Sales Summary / Annex E-1 minimum content semantics:
  - Report Date.
  - Beginning SI Number.
  - Ending SI Number.
  - Previous Grand Total.
  - Present Grand Total.
  - Sales for the Day.
  - Gross Sales.
  - Net Sales.
  - VATable Sales.
  - VAT Amount.
  - VAT Exempt Sales.
  - Zero-Rated Sales.
  - Discounts.
  - Voids.
  - Returns.
  - Reset Counter.
  - Z Counter.
- Supported output/export mode semantics for BIR Sales Summary should include Print, PDF, and JSON.

Exact Diplomat reporting treatment, mandatory formats, and final layouts remain open for BIR/accounting/accreditation confirmation.

## 19. EJ, POSLog, and Export API Family

Provisional route families:

- `/v1/pos/exports/*`
- `/v1/pos/reports/exports/*`

This family covers EJ export, POSLog export, fiscal exports, report exports, and export status.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Request EJ export | Generate EJ export for approved period/scope. | Provisional |
| Request POSLog export | Generate POSLog export in confirmed format. | Provisional |
| Request fiscal export | Generate approved fiscal export package. | Provisional |
| Get export status | Retrieve export generation state and output reference. | Provisional |
| Get export validation status | Retrieve structured export validation state and audit reference where applicable. | Provisional |
| Retrieve export | Retrieve export output according to authorization and retention policy. | Provisional |

### Contract Semantics

- EJ, POSLog, reports, exports, audit records, printed SI, and digital SI shall reconcile to canonical fiscal records.
- Export generation shall be auditable.
- Export access shall be authorized and auditable.
- Final mandatory formats remain open.
- Candidate formats may include text replica, PDF or equivalent human-readable export, JSON or equivalent structured export, POSLog, ARTS POSLog 6.x-aligned export where practical and accepted by BIR/accreditation requirements, BIR Sales Summary, and Annex E report exports.
- ARTS POSLog is a structured export/schema interoperability reference.
- ARTS POSLog does not replace Philippine BIR fiscal document/report requirements.
- ExitPass shall preserve Sales Invoice, SI, and Sales Invoice Number terminology.
- ExitPass shall preserve BIR-required outputs such as Sales Invoice, X-read, Z-read, EJ, POSLog, and BIR Sales Summary.
- Local/BIR-specific fields may be represented as local extensions or mapped fields where needed.
- Candidate local/BIR extension or mapping concepts include Sales Invoice Number, Ticket Number / Plate Number, Site / branch / business unit identity, channel / terminal / workstation identity, Business Day Date, MIN, PTU, Serial Number, supplier/accreditation metadata, Reset Counter, Z Counter, Grand Total Amount, Digital SI URL, parking session timestamps and duration, and fiscal audit references.
- JSON fiscal and audit records should remain complete even when printed outputs are simplified.
- JSON and POSLog exports should be schema-versioned.
- JSON and POSLog exports should support validation against approved BIR/ARTS-aligned schemas where applicable.
- Export validation success, failure, and pending states shall be auditable and visible to operational/support workflows.

Final ARTS POSLog profile, schema mapping, JSON schema versioning strategy, validation job implementation, storage model, packaging format, export formats, and retention/access rules remain open for BIR/accounting, compliance, security/privacy, Database Design, and Engineering Pack confirmation.

## 20. Fiscal Reset and Recovery API Family

Provisional route family: `/v1/pos/recovery/*`

This family covers fiscal reset request/approval/status, recovery continuity check/status, and supervised recovery workflow.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Request fiscal reset | Request a fiscal reset event. | Provisional |
| Approve fiscal reset | Supervisor/fiscal admin approval workflow where required. | Provisional |
| Get reset status | Retrieve reset request/completion status and audit reference. | Provisional |
| Run recovery continuity check | Check counters, GTA, SI sequence, EJ hash, and last event timestamp continuity. | Provisional |
| Request supervised recovery | Initiate controlled recovery where continuity cannot be proven. | Provisional |
| Get recovery status | Retrieve recovery state and audit reference. | Provisional |

### Contract Semantics

Fiscal reset contract shall support:

- Previous Grand Total Amount.
- Previous reset counter.
- Reset timestamp.
- Reset reason.
- Approving user.
- Recovery/reference notes.
- Audit reference.

Recovery contract shall enforce:

- No resume from lower fiscal counter.
- No resume from lower Grand Total Amount.
- No resume from lower Z-counter.
- No resume from earlier Sales Invoice sequence.
- No resume from broken EJ hash continuity.
- No resume from earlier last fiscal event timestamp.
- Supervised recovery and recovery audit record when continuity cannot be proven.

Offline fiscal issuance remains disabled or restricted by default until BIR/accounting approves a compliant model.

## 21. Exception and Retry Status API Family

Provisional route family: `/v1/pos/exceptions/*`

This family covers fiscal issuance failure/timeout, controlled retry, exception status, and controlled closure.

### Candidate Operations

| Candidate operation | Contract purpose | Status |
| --- | --- | --- |
| Create fiscal issuance exception | Record controlled exception when SI issuance fails or times out. | Provisional |
| Get exception/retry status | Retrieve pending, retrying, failed, closed, or recovery-blocked status. | Provisional |
| Request retry | Trigger controlled retry without duplicate fiscal document creation. | Provisional |
| Close exception | Close with successful SI issuance or controlled exception closure. | Provisional |

### Contract Semantics

- Payment finality is not automatically reversed.
- Central PMS shall not issue ExitAuthorization while fiscal issuance remains failed, timed out, or pending unless controlled exception policy allows release.
- Manual release, if allowed by policy, requires supervisor approval, incident tag, and reconciliation tag.
- POS Server still shall not issue ExitAuthorization.
- Exception closure shall be auditable.

## 22. Audit and Event API Impact

This contract identifies audit/event impact but does not define final event schemas.

Events or audit records may be needed for:

- SI issuance requested.
- SI issued.
- SI issuance failed/timed out.
- Digital SI URL created.
- Digital SI accessed where required.
- Reprint requested/completed/failed.
- Adjustment requested/issued/rejected.
- X-read generated.
- Z-read generated.
- BIR Summary generated.
- Annex E generated.
- EJ/POSLog/export generated.
- Structured export validation pending/passed/failed.
- POS Server ONLINE/OFFLINE status changed where required.
- Channel/terminal ONLINE/OFFLINE or equivalent health status changed where applicable.
- Fiscal reset requested/approved/completed.
- Terminal/channel registered/updated.
- Fiscal identity changed.
- Recovery continuity check passed/failed.
- Supervised recovery approved/completed.
- Fiscal exception opened/retried/closed.

POS/fiscal events are audit, integration, and observability signals. POS/fiscal event publication does not grant payment finality, does not issue or imply ExitAuthorization, and must not be treated by consumers as payment or exit authority.

Final event names, payloads, outbox ownership, replay behavior, delivery guarantees, and retention remain open for API Contract Pack and Engineering Pack.

## 23. WebPay Integration Contract

WebPay shall route fiscal issuance through Central PMS and the resolved Site POS Server.

WebPay contract responsibilities:

- Must not declare platform payment finality.
- Must not issue ExitAuthorization.
- Must not act as an independent POS system.
- May display/provide access to issued SI after POS Server issuance.
- May support digital SI URL presentation.
- Must preserve Central PMS payment finality and ExitAuthorization authority.

Open:

- WebPay fiscal terminal identity without physical printer or hardware serial.
- WebPay receipt of the digital SI URL.
- Public/customer SI URL access model.

## 24. APM Integration Contract

APM shall be modeled as a child terminal/channel under the Site POS Server.

APM contract responsibilities:

- Route fiscal issuance to resolved Site POS Server through approved flow.
- Present or print POS Server-issued SI according to approved printing model.
- Convert the POS Server-returned digital SI URL into a QR code and display or print that QR code where supported.
- Preserve Central PMS payment finality and ExitAuthorization authority.
- Not become independent fiscal authority for the Site.
- Not issue ExitAuthorization.
- Not bypass Central PMS.

Open:

- Whether APM prints POS Server-issued payload or requires another approved printing arrangement.
- APM hardware serial/fiscal identity assignment.

## 25. Cashier POS Integration Contract

Cashier POS shall be modeled as a child terminal/channel under the Site POS Server.

Cashier POS contract responsibilities:

- Use Site POS Server fiscal APIs for issuance presentation where applicable.
- Preserve cashier/session accountability.
- Support controlled reprint and adjustment requests only for authorized roles.
- Present printed SI where applicable.
- Present digital SI URL and perform channel-side QR generation/display/print where supported.
- Display fiscal status and exception messaging.
- Not independently declare payment finality outside Central PMS authority.
- Not issue ExitAuthorization.

Open:

- Cashier/session context contract.
- Role/permission matrix for cashier, supervisor, and fiscal administrator actions.

## 26. EC Device / Continuity Terminal Integration Contract

EC Device / Continuity Terminal shall use the same Site POS Server fiscal authority when activated.

EC/continuity contract responsibilities:

- Register as a child terminal/channel under Site POS Server.
- Preserve Central PMS payment finality and ExitAuthorization authority.
- Support digital SI URL presentation and channel-side QR generation/display/print where approved.
- Preserve fiscal sequence and counter continuity.
- Not create offline fiscal documents unless a BIR/accounting-approved model defines sequence, counter, evidence, reconciliation, and recovery controls.

Open:

- Offline fiscal issuance allowance, if any.
- Continuity sequence/counter model.
- Evidence and reconciliation controls.
- Continuity presentation contract.

## 27. Operator-assisted Integration Contract

Operator-assisted payment, if allowed, shall route fiscal issuance through the resolved Site POS Server.

Operator-assisted contract responsibilities:

- Preserve operator identity.
- Preserve Site context.
- Preserve reason/context where required.
- Support SI presentation, digital SI URL presentation, and operator-terminal QR generation/display/print where supported.
- Preserve Central PMS payment finality and ExitAuthorization authority.
- Not allow operator to declare platform payment finality outside Central PMS authority.
- Not allow operator to issue ExitAuthorization through POS Server.

Open:

- Operator terminal presentation rules.
- Whether QR presentation is mandatory for assisted channels.
- Manual release policy integration after fiscal issuance failure.

## 28. Future Channel Contract Pattern

Future payment channels shall follow the same Site POS Server pattern.

Future channel contract requirements:

- Register as child channel/terminal under Site POS Server.
- Provide or reference resolved Site context through Central PMS authority.
- Preserve Central PMS payment finality and ExitAuthorization authority.
- Route fiscal issuance through the resolved Site POS Server.
- Receive fiscal document identity/status and digital SI URL where applicable.
- Convert the POS Server-returned digital SI URL into a QR code where channel capability and policy allow.
- Not become an independent POS system for the Site.

Future channels shall not require a new fiscal authority model unless approved by BRD/System Design governance.

## 29. Status Model

This section defines proposed status taxonomy at planning-contract level. Final status values remain pending API Contract review and future Database Design alignment.

| Status concept | Intended use |
| --- | --- |
| Fiscal issuance requested | Issuance request accepted or recorded. |
| Issued | Fiscal document successfully issued. |
| Failed | Operation failed and requires handling. |
| Timed out | Caller did not receive a completion response within the expected window. This is not successful issuance. |
| Completion unknown | Caller cannot safely determine whether issuance completed and must query status before retrying or authorizing exit. This is not successful issuance. |
| Retry pending | Controlled retry is queued or expected. |
| Pending recovery | Operation blocked by recovery/continuity state. |
| Blocked | Operation cannot proceed due to policy, configuration, authorization, or fiscal state. |
| Cancelled | Request cancelled according to allowed workflow. |
| Duplicate / idempotent replay | Duplicate request recognized under idempotency model. |
| Reprint requested | Reprint request accepted or recorded. |
| Reprint completed | Reprint completed and audited. |
| Reprint failed | Reprint request failed or was blocked and requires handling. |
| Adjustment requested | Adjustment request accepted or recorded. |
| Adjustment issued | Adjustment fiscal document issued. |
| Adjustment rejected | Adjustment request rejected. |
| Report requested | Report generation requested. |
| Report generated | Report generated successfully. |
| Report failed | Report generation failed. |
| Export requested | Export requested. |
| Export generated | Export generated successfully. |
| Export failed | Export generation failed. |
| Export validation pending | Structured export validation is queued or in progress. |
| Export validation passed | Structured export validation passed against the approved validation profile. |
| Export validation failed | Structured export validation failed and requires operational/support handling. |
| Reset requested | Fiscal reset requested. |
| Reset approved | Fiscal reset approved. |
| Reset completed | Fiscal reset completed. |
| Reset rejected | Fiscal reset rejected. |
| Recovery check passed | Fiscal continuity check passed. |
| Recovery check failed | Fiscal continuity check failed. |
| Digital SI URL active | Digital SI URL is currently accessible according to policy. |
| Digital SI URL expired | Digital SI URL has expired according to policy. |
| Digital SI URL revoked | Digital SI URL has been revoked or blocked according to policy. |
| POS Server online | POS Server is reachable/healthy according to approved operational status rules. |
| POS Server offline | POS Server is unreachable/unhealthy according to approved operational status rules. This does not approve offline fiscal issuance. |
| Channel/terminal online | Channel or terminal is reachable/healthy according to approved operational status rules. |
| Channel/terminal offline | Channel or terminal is unreachable/unhealthy according to approved operational status rules. This does not approve offline fiscal issuance. |

Timeout and completion-unknown states must not be treated as successful fiscal issuance. Central PMS shall not issue ExitAuthorization based only on timed-out or completion-unknown status. Idempotent status lookup and retry semantics must prevent duplicate Sales Invoice issuance.

## 30. Open Questions

| ID | Open question | Owner / dependency |
| --- | --- | --- |
| API-OQ-001 | Final endpoint route family naming. | Architecture/API owners |
| API-OQ-002 | Request/response DTO boundaries and shared metadata conventions. | Architecture/API owners |
| API-OQ-003 | Idempotency key scope for Sales Invoice issuance. | Architecture, Central PMS owners, BIR/accounting |
| API-OQ-004 | Duplicate issuance handling. | Architecture/API owners |
| API-OQ-005 | Sequence-gap, reserved-number, failed-issuance, and abandoned-issuance behavior. | BIR/accounting, architecture |
| API-OQ-006 | Digital SI URL token/access model. | Security/privacy, architecture |
| API-OQ-007 | Digital SI URL expiry policy. | Security/privacy, compliance |
| API-OQ-008 | Public/customer SI URL authentication/access model. | Security/privacy, compliance |
| API-OQ-009 | Digital SI URL access and re-access audit treatment. | Security/privacy, compliance |
| API-OQ-010 | WebPay fiscal terminal identity. | BIR/accounting, architecture |
| API-OQ-011 | APM printing model. | BIR/accounting, APM vendor, architecture |
| API-OQ-012 | Terminal/channel registry fields. | Architecture, database design, security |
| API-OQ-013 | Fiscal identity fields and change authorization. | BIR/accounting, security, compliance |
| API-OQ-014 | X-read and Z-read scope. | BIR/accounting, finance, operations |
| API-OQ-015 | Exact report/export formats and layouts. | BIR/accounting, compliance |
| API-OQ-016 | Exact ARTS POSLog profile and schema mapping. | BIR/accreditation, architecture, database design, Engineering Pack |
| API-OQ-017 | Exact JSON schema versioning strategy. | Architecture, database design, Engineering Pack |
| API-OQ-018 | Exact accreditation sample package. | BIR/accreditation, compliance |
| API-OQ-019 | Fiscal adjustment workflow sequencing. | Payments, finance, compliance, architecture |
| API-OQ-020 | Refund/reversal relationship with Central PMS/provider. | Payments, finance, architecture |
| API-OQ-021 | Recovery continuity API. | Architecture, security, operations, database design |
| API-OQ-022 | Offline fiscal issuance approval and restriction representation, if any. | BIR/accounting, architecture, operations |
| API-OQ-023 | Audit/event publication contracts. | Architecture, Engineering Pack, security |
| API-OQ-024 | Canonical error/status model finalization. | Architecture/API owners |
| API-OQ-025 | Security/RBAC model for high-risk APIs. | Security/privacy, compliance, operations |
| API-OQ-026 | MIN/PTU/serial/software/supplier assignment. | BIR/accounting, compliance, architecture |
| API-OQ-027 | VAT/tax treatment by Site, taxpayer, transaction type, entitlement, and line item. | BIR/accounting, finance |
| API-OQ-028 | Diplomat VAT treatment, evidence, reporting, and retention. | BIR/accounting, security/privacy, compliance |

## 31. Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| API contract grants ExitAuthorization authority to POS Server | Core authority violation. | Keep ExitAuthorization outside all POS Server route families. |
| POS Server API declares payment finality | Central PMS authority violation. | Treat payment finality as Central PMS context only. |
| Weak idempotency | Duplicate SI or inconsistent fiscal reference. | Require `Idempotency-Key` for side-effecting operations and define retry semantics. |
| Sequence gaps handled incorrectly | BIR/accreditation or audit failure. | Keep sequence-gap behavior open for BIR/accounting and API review. |
| Digital SI URL overexposes data | Privacy/security breach. | Keep public/customer access model separate and subject to Security/Privacy Review. |
| QR presentation becomes APM-only | Future channels diverge from platform model. | Treat QR as channel/terminal capability. |
| APM printing model conflicts with Site POS Server authority | Split fiscal authority or failed certification. | Keep POS Server as issuer and resolve APM print behavior with BIR/vendor confirmation. |
| Offline issuance is implied by continuity APIs | Duplicate/skipped fiscal sequences. | Keep offline issuance disabled/restricted until approved. |
| Adjustment API owns refund finality | Payment authority confusion. | Keep Central PMS/provider as refund/reversal finality authority. |
| Database details leak into API contract too early | Overcoupled design. | Define contract semantics without tables/columns/migrations. |
| Export format is finalized prematurely | Compliance mismatch. | Keep export formats open until BIR/accounting confirmation. |

## 32. Appendices

### Appendix A: Provisional Route Family Summary

| Route family | Purpose | Status |
| --- | --- | --- |
| `/v1/pos/fiscal-issuance/*` | Sales Invoice issuance and issuance status. | Provisional |
| `/v1/pos/fiscal-documents/*` | Fiscal document lookup/status and document references. | Provisional |
| `/v1/pos/digital-si/*` | Digital SI URL and access status for channel/customer presentation. | Provisional |
| `/v1/pos/channels/*` | Channel/terminal registration and status. | Provisional |
| `/v1/pos/fiscal-identity/*` | Fiscal identity configuration and readiness. | Provisional |
| `/v1/pos/reprints/*` | Reprint request/status/history. | Provisional |
| `/v1/pos/adjustments/*` | Void/refund/cancel/return fiscal adjustment workflows. | Provisional |
| `/v1/pos/reports/*` | X/Z, BIR Summary, Annex E, and report status/export. | Provisional |
| `/v1/pos/exports/*` | EJ, POSLog, fiscal export, and export retrieval. | Provisional |
| `/v1/pos/recovery/*` | Fiscal reset, continuity check, and supervised recovery. | Provisional |
| `/v1/pos/exceptions/*` | Fiscal exception/retry status and controlled closure. | Provisional |

### Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| APM | AutoPay Machine |
| BIR | Bureau of Internal Revenue |
| BRD | Business Requirements Document |
| DR | Disaster Recovery |
| EC | Emergency/Exception/Continuity, pending final product terminology |
| EJ | Electronic Journal |
| GTA | Grand Total Amount |
| MIN | Machine Identification Number |
| PMS | Parking Management System |
| POS | Point of Sale |
| PTU | Permit to Use |
| QR | Quick Response code |
| RBAC | Role-Based Access Control |
| SI | Sales Invoice |
| VAT | Value-Added Tax |

### Appendix C: Non-Decisions

This draft does not decide:

- Final endpoint paths.
- Final DTO schemas.
- Final database tables, columns, indexes, constraints, or migrations.
- Final event schemas or outbox design.
- Final idempotency persistence model.
- Final fiscal numbering pattern.
- Final sequence-gap handling.
- Final digital SI URL token/access/expiry/authentication model.
- Final channel/terminal QR rendering implementation details.
- Final MIN/PTU/serial/software/supplier assignment.
- Final WebPay fiscal identity.
- Final APM printing model.
- Final X/Z scope.
- Final report/export formats.
- Final security/RBAC matrix.
- Offline fiscal issuance approval.
