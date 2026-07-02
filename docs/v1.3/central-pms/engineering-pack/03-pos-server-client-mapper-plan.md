# Central PMS Fiscal Issuance Engineering Pack Detail Plan 03: POS Server Client and Request Mapper Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | POS Server Client and Request Mapper Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines the Central PMS POS Server client and mapper planning boundary. It does not create endpoint specs, DTO classes, source code, SQL, migrations, or generated artifacts.

## Purpose

Central PMS needs a controlled integration client for POS Server fiscal issuance and readback. The mapper must translate Central PMS payment, session, payable-basis, Site, and fiscal facts into the current POS Server API contract without leaking sensitive payloads or changing authority boundaries.

## Client Abstraction

The future client should provide planning-level operations for:

- create fiscal document using `POST /v1/fiscal-documents/`
- read fiscal document using `GET /v1/fiscal-documents/{fiscalDocumentId}`
- parse success, replay, conflict, failure, and unknown outcomes
- preserve correlation ids and response metadata
- avoid treating POS Server as payment or exit authority

Final class names, methods, generated clients, and transport libraries remain deferred.

## Configuration Planning

Candidate configuration areas:

- Site POS Server base URL
- service-to-service authentication configuration
- connect/request timeout posture
- retry enablement posture
- maximum payload size
- circuit breaker or service health behavior
- environment and Site rollout enablement

Exact keys, secrets storage, and service discovery remain deferred. No secrets should be stored in documentation, logs, prompts, or source-controlled configuration.

## Request Construction from Central PMS State

The mapper should build the POS Server request from approved Central PMS state only:

- resolved Site and Site POS Server context
- Central PMS parking session reference
- Central PMS payment attempt reference
- Central PMS payment confirmation reference
- approved payable basis
- statutory discount validation reference where applicable
- document lines
- tenders
- tax details
- totals
- reference context

## Field Mapping Plan

Candidate mappings:

| POS Server request area | Central PMS source |
| --- | --- |
| `sitePosServerId` | resolved Site POS Server identifier |
| `sitePosServerRef` | stable Site POS Server reference |
| fiscal document type/status | configured fiscal issuance policy for Sales Invoice |
| `businessDayDate` | Site business-day context where available |
| `centralPmsParkingSessionRef` | Central PMS parking session reference |
| `centralPmsPaymentAttemptRef` | Central PMS payment attempt reference |
| `centralPmsPaymentConfirmationRef` | Central PMS payment finality reference |
| `payableBasis.payableBasisRef` | approved payable-basis reference |
| `payableBasis.upstreamFinalityRef` | stable fiscal issuance idempotency source |
| `payableBasis.currencyCode` | approved payment currency |
| `payableBasis.payableAmountMinorUnits` | approved paid amount in minor units |
| document lines | approved fiscal line facts |
| tenders | payment tender facts from Central PMS payment context |
| tax details | approved fiscal tax facts |
| totals | approved fiscal totals |
| discount references | approved Central PMS / Discount workflow references |

## Upstream Finality Reference Rules

Central PMS must use the same `payableBasis.upstreamFinalityRef` for the same fiscal issuance attempt. It must not reuse that value for a semantically different issuance, and it must not generate a new value merely to bypass an idempotency conflict.

## Sensitive Payload Exclusion

The mapper must not send:

- raw provider callback payloads
- card PAN/CVV
- tokens
- secrets
- credentials
- raw entitlement evidence
- uncontrolled uploaded evidence files
- raw customer identifiers beyond approved fiscal/reference fields

Evidence references and approved validation references may be passed only where the POS Server API contract supports them.

## Correlation ID Propagation

The client should propagate or generate correlation values for:

- Central PMS request id
- payment confirmation reference
- upstream finality reference
- parking session reference
- Site and Site POS Server
- POS Server fiscal document id when known

Correlation data must be safe for logs and audit records.

## Response Parsing Responsibilities

The client should parse:

- HTTP status
- response code
- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- `fiscalDocumentStatusCodeId`
- fiscal identity and numbering fields
- `errorPosture`
- conflict and validation error codes
- fiscal document id from successful or failed responses when present

Parsing must not decide ExitAuthorization. It returns structured outcome to the orchestration service.

## GET Readback Client Behavior

The readback client should support:

- read by POS Server fiscal document id
- parsing persisted fiscal numbering fields
- confirming evidence/assignment state
- detecting not found or service failure
- returning inconclusive result without inventing success

GET readback means persisted POS Server fiscal document readback only. It is not payment finality, ExitAuthorization, BIR report finality, or gate permission.

## Test Fixture Needs

Future tests should include fixtures for:

- `202 accepted` + `newly_created`
- `202 accepted` + `idempotent_replay`
- `409 fiscal_document_idempotency_conflict`
- request correction failure
- fiscal configuration correction failure
- service recovery failure
- `fiscal_number_assignment_incomplete`
- GET success with complete evidence
- GET not found
- GET service unavailable
- malformed or missing required response fields

## Risks and Open Questions

- Final service-to-service authentication mechanism is not defined.
- Timeout and retry settings are not finalized.
- Request normalization should match POS Server semantic hash expectations.
- Final mapping for every fiscal line/tax/tender field needs engineering confirmation.

## Authority Boundary

The client and mapper are integration plumbing. They do not create payment finality, approve entitlement, issue Sales Invoices, issue ExitAuthorization, open gates, activate continuity, or approve manual release.
