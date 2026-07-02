# ExitPass POS Server API Contract v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass POS Server API Contract |
| Version | v1.0, v1.3 runtime-aligned |
| Product scope | ExitPass v1.3 POS Server |
| Status | Aligned to current POS Server runtime inspection |
| Output format | Markdown only |
| Runtime baseline inspected | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |
| Primary implementation scope | Fiscal document create/read API |

## 2. Purpose and Scope

This API Contract documents the current supported POS Server runtime API behavior for ExitPass v1.3.

The current runtime-supported API surface is:

| Method | Route | Status |
| --- | --- | --- |
| `POST` | `/v1/fiscal-documents/` | Implemented |
| `GET` | `/v1/fiscal-documents/{fiscalDocumentId}` | Implemented |

This contract distinguishes:

- currently implemented runtime behavior
- contract behavior Central PMS may depend on now
- planned or deferred API families
- explicit non-authority boundaries

This document does not define database schema, SQL implementation, source code, generated artifacts, tests, deployment scripts, UAT scripts, or runbook procedures.

## 3. Approved Baseline Inputs

| Reference | Role |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Platform authority and business baseline. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | v1.3 platform architecture baseline. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approved BRD baseline status. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | POS/Invoicing business and fiscal baseline. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | POS Server system design baseline. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_Repo_Inspection_Report.md` | Prior implementation reality-check. |
| `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\FiscalDocuments` | Current API DTO and endpoint source. |
| `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Runtime\FiscalDocuments` | Current runtime behavior source. |
| `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Persistence.Postgres\FiscalDocuments` | Current persistence behavior source. |
| `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime` | Runtime implementation slice notes. |

## 4. Authority Boundaries

POS Server is the resolved Site fiscal issuance authority only.

POS Server owns, within current implemented scope:

- fiscal document creation for the resolved Site POS Server
- server-side fiscal identity resolution
- server-side fiscal sequence policy resolution
- fiscal sequence allocation
- fiscal document number formatting
- fiscal document persistence and readback
- fiscal document child facts accepted by the current runtime
- idempotent fiscal issuance replay/conflict behavior

POS Server does not:

- declare platform payment finality
- interact with payment providers as payment authority
- own Vendor PMS session lifecycle or tariff computation
- approve statutory entitlement
- mutate Central PMS payable basis directly
- record Central PMS fiscal issuance reference on behalf of Central PMS
- issue `ExitAuthorization`
- open gates
- activate continuity
- approve manual release
- operate as Operator Console, Management Dashboard, Assisted Payment Terminal, WebPay, APM, or Central PMS

Central PMS remains authority for:

- payment finality
- payment-linked platform state
- payable-basis readiness before fiscal issuance
- recording returned POS Server fiscal issuance reference
- normal `ExitAuthorization` after fiscal prerequisites are satisfied
- degraded resolve and continuity decisioning under approved policy

## 5. Authentication and Authorization Placeholder

Current inspected runtime wiring focuses on fiscal document create/read behavior and persistence configuration.

Authentication and authorization are not finalized in this API Contract. Until a later security/API contract task confirms the model:

- Central PMS should be treated as the intended trusted caller for fiscal issuance.
- Public clients, payment channels, terminals, dashboards, and Operator Console must not call POS Server as fiscal authority directly unless a later approved gateway/service boundary permits it.
- Final caller authentication, service identity, mTLS/token model, role claims, and network trust controls remain deferred.

## 6. Common Headers and Transport Metadata

Current runtime behavior verified from code does not require a separate `Idempotency-Key` HTTP header.

Recommended but not currently enforced headers:

| Header | Current status | Notes |
| --- | --- | --- |
| `X-Correlation-Id` | Recommended / not verified as enforced | For traceability across Central PMS, POS Server, and operations logs. |
| `X-Request-Id` | Recommended / not verified as enforced | For caller-side diagnostics. |
| `Idempotency-Key` | Not currently the runtime source of idempotency | Current runtime derives idempotency key from upstream finality reference. Do not document this as required until implemented. |

The implemented idempotency source is request-body data, not an HTTP header.

## 7. Current Response Envelope

The current API uses endpoint-specific JSON response records.

### 7.1 Create Response Envelope

Current `CreateFiscalDocumentResponse` shape:

```json
{
  "succeeded": true,
  "code": "accepted",
  "message": "Fiscal document creation accepted for persistence.",
  "fiscalDocumentId": "00000000-0000-0000-0000-000000000000",
  "fiscalIdentityId": "00000000-0000-0000-0000-000000000000",
  "fiscalSequencePolicyId": "00000000-0000-0000-0000-000000000000",
  "fiscalSequenceValue": 1,
  "fiscalDocumentNumber": "SI-00000001",
  "fiscalSeries": "SI",
  "fiscalNumberPrefixText": "SI-",
  "fiscalNumberSuffixText": null,
  "fiscalNumberAssignedAt": "2026-07-01T00:00:00Z",
  "fiscalNumberAssignedByRef": "pos-server:system"
}
```

`httpStatusCode` exists in the runtime response record as a server-side JSON-ignored field and is not part of the JSON body.

### 7.2 Read Response Envelope

Current `GetFiscalDocumentResponse` shape:

```json
{
  "succeeded": true,
  "code": "found",
  "message": "Fiscal document found.",
  "document": {
    "fiscalDocumentId": "00000000-0000-0000-0000-000000000000"
  }
}
```

The `document` object is the current `FiscalDocumentReadModel` and includes header fields plus child collections.

## 8. Idempotency Contract

### 8.1 Current Runtime Idempotency Source

Current runtime derives idempotency internally:

| Element | Current runtime source |
| --- | --- |
| Idempotency scope | `fiscal_document_creation:{sitePosServerId:N}:{fiscalDocumentTypeCodeId:N}` |
| Idempotency key | `payableBasis.upstreamFinalityRef` after trimming |
| Semantic request hash | Server-computed SHA-256 over normalized fiscal issuance request fields |

The caller does not currently supply an `Idempotency-Key` header as the runtime source of idempotency.

### 8.2 Caller Contract

Central PMS shall:

- use a stable `payableBasis.upstreamFinalityRef` for the same fiscal issuance attempt
- retry uncertain network outcomes with the same semantic request body and upstream finality reference
- not reuse the same upstream finality reference for a semantically different fiscal issuance request
- treat an idempotency conflict as fail-closed

### 8.3 Replay Behavior

Same idempotency scope + same idempotency key + same semantic request hash:

- replays the original fiscal document result
- returns the original fiscal document id and fiscal numbering fields
- does not allocate a new fiscal number
- does not advance the fiscal sequence again

Current runtime response code for replay is still `accepted` with HTTP `202 Accepted`; the message distinguishes replay as `Fiscal document creation replayed from idempotency record.`

### 8.4 Conflict Behavior

Same idempotency scope + same idempotency key + different semantic request hash:

- fails closed
- returns code `fiscal_document_idempotency_conflict`
- returns HTTP `409 Conflict`
- does not create a new fiscal document
- does not allocate a new fiscal number
- does not advance sequence state

## 9. Semantic Request Hash Posture

The semantic request hash is server-computed and deterministic.

The current hash normalizes request content including:

- Site POS Server id/ref
- fiscal document type/status ids and type key
- channel terminal id
- business day date
- Central PMS parking/payment references
- payable basis
- discount references
- document links
- document lines
- tenders
- tax details
- discount privilege details
- totals
- reference context
- payment finality reference
- vendor acknowledgment reference

The current hash excludes transport headers and does not rely on JSON field order supplied by the caller.

Central PMS must treat semantically different fiscal issuance requests as requiring different upstream finality/idempotency identity.

## 10. Fiscal Document Creation Endpoint

### 10.1 Endpoint

| Field | Value |
| --- | --- |
| Method | `POST` |
| Route | `/v1/fiscal-documents/` |
| Current success HTTP status | `202 Accepted` |
| Current success code | `accepted` |
| Current conflict HTTP status | `409 Conflict` |
| Current validation failure HTTP status | `400 Bad Request` |
| Current persistence/configuration failure HTTP status | `503 Service Unavailable` |

### 10.2 Purpose

Creates a numbered fiscal document for the resolved Site POS Server after Central PMS has verified payment finality and prepared the approved payable basis.

The endpoint performs fiscal issuance work. It is not payment confirmation and is not exit authorization.

### 10.3 Central PMS Preconditions

Before calling this endpoint, Central PMS shall have:

- resolved the Site
- determined the Site POS Server context
- verified and recorded platform payment finality
- established approved payable basis
- completed discount validation through Central PMS / Discount workflow where statutory discount treatment applies
- prepared fiscal facts needed by POS Server

### 10.4 Request Body Shape

Current `CreateFiscalDocumentRequest` fields:

| Field | Type | Required by runtime validation | Notes |
| --- | --- | --- | --- |
| `sitePosServerRef` | string | Yes | Local fiscal context reference; nonblank required. |
| `fiscalDocumentTypeCodeKey` | string | Yes | Fiscal document type key; nonblank required. |
| `payableBasis` | object | Yes | Contains approved upstream payable-basis and finality reference. |
| `sitePosServerId` | UUID | Yes | Used for fiscal identity, policy, and idempotency scope. |
| `channelTerminalId` | UUID/null | No | Channel/terminal reference under Site POS Server. |
| `fiscalDocumentTypeCodeId` | UUID | Yes | Used for policy resolution and idempotency scope. |
| `fiscalDocumentStatusCodeId` | UUID | Yes | Initial fiscal document status code. |
| `businessDayDate` | date/null | No | Business day context. |
| `centralPmsParkingSessionRef` | string/null | No | Central PMS reference only. |
| `centralPmsPaymentAttemptRef` | string/null | No | Central PMS reference only. |
| `centralPmsPaymentConfirmationRef` | string/null | No | Central PMS reference only. |
| `upstreamFinalityRef` | string/null | Conditionally | Used as payable basis upstream finality fallback. |
| `paymentFinalityRef` | string/null | No | Stored fiscal reference context only. |
| `vendorAckRef` | string/null | No | Vendor acknowledgment context only. |
| `documentLinks` | array/null | No | Fiscal document links. |
| `documentLines` | array/null | Yes, unless `lines` used | At least one line required after alias resolution. |
| `lines` | array/null | Yes, unless `documentLines` used | Alias supported by current runtime. |
| `tenders` | array/null | Yes | At least one tender required. |
| `taxDetails` | array/null | No | Tax detail facts. |
| `discountPrivilegeDetails` | array/null | No | Fiscal discount/VAT privilege details. |
| `totals` | array/null | No | Fiscal totals. |
| `referenceContext` | object/null | No | Key/value reference context only. |

### 10.5 Payable Basis Object

Current `FiscalizationPayableBasisRequest` fields:

| Field | Type | Required by runtime validation | Notes |
| --- | --- | --- | --- |
| `payableBasisRef` | string | Yes | Approved upstream payable-basis reference. |
| `upstreamFinalityRef` | string/null | Yes, directly or via top-level fallback | Idempotency key source after mapping. |
| `currencyCode` | string | Yes | Three uppercase letters after normalization. |
| `payableAmountMinorUnits` | integer | Yes | Minor units. |
| `discountReferences` | array/null | No | Used to validate statutory discount treatment. |
| `referenceContext` | object/null | No | Reference context only. |

If `payableBasis.upstreamFinalityRef` is blank, the current mapper uses top-level `upstreamFinalityRef`.

### 10.6 Fiscal Document Facts Accepted

Current runtime accepts and persists:

- fiscal document header context
- document status history
- document links
- fiscal lines
- fiscal tenders
- fiscal tax details
- fiscal discount privilege details
- fiscal totals

Current runtime rejects:

- missing payable basis
- missing upstream finality reference
- statutory discount treatment without approved discount reference
- invalid fiscal line data
- missing or invalid fiscal tender data
- invalid fiscal tax detail data
- invalid fiscal discount privilege detail data
- invalid fiscal total data
- raw sensitive evidence markers, credential markers, token/secret markers, card number/CVV markers, raw payment payload markers, or provider callback markers in accepted text/context fields

### 10.7 Server-Side Fiscal Identity and Policy Resolution

The caller provides Site POS Server and fiscal document type context, but the runtime resolves fiscal identity and sequence policy server-side.

Current runtime behavior:

- selects exactly one eligible fiscal identity for the Site POS Server
- fails closed if no fiscal identity exists
- fails closed if fiscal identity relationship exists but none is currently eligible/effective
- fails closed if more than one eligible fiscal identity is found
- selects exactly one eligible fiscal sequence policy for Site POS Server and fiscal document type
- fails closed if no sequence policy exists
- fails closed if sequence policy exists but none is currently eligible/effective
- fails closed if more than one eligible fiscal sequence policy is found

Current failure codes include:

- `fiscal_identity_not_found`
- `fiscal_identity_not_effective`
- `fiscal_identity_ambiguous`
- `fiscal_sequence_policy_not_found`
- `fiscal_sequence_policy_not_effective`
- `fiscal_sequence_policy_ambiguous`

### 10.8 Sequence Allocation Behavior

Current runtime behavior:

- selects the eligible `pos.fiscal_sequence_states` row for the resolved policy
- locks the sequence-state row with row-level locking using `FOR UPDATE`
- computes next sequence value from the locked current sequence value
- formats the fiscal document number using resolved policy code/prefix/suffix/padding
- persists fiscal document number fields on `pos.fiscal_documents`
- updates sequence state after fiscal document child facts are inserted and before commit
- completes the idempotency record in the same transaction
- returns fiscal number fields only after durable commit

Current failure codes include:

- `fiscal_sequence_state_not_found`
- `fiscal_sequence_state_not_effective`
- `fiscal_number_allocation_failed`
- `fiscal_document_number_format_failed`

Pre-commit rollback does not durably advance sequence state. Committed fiscal numbers must not be reused.

Durable post-commit gap/recovery policy remains deferred for BIR/accounting and downstream recovery design.

### 10.9 Successful Response

Success and replay both currently return HTTP `202 Accepted` and code `accepted`.

Successful response fields:

| Field | Meaning |
| --- | --- |
| `succeeded` | `true` for created or replayed fiscal document result. |
| `code` | Current value `accepted`. |
| `message` | Creation or replay message. |
| `fiscalDocumentId` | POS Server fiscal document id. |
| `fiscalIdentityId` | Resolved fiscal identity id. |
| `fiscalSequencePolicyId` | Resolved fiscal sequence policy id. |
| `fiscalSequenceValue` | Allocated sequence value. |
| `fiscalDocumentNumber` | Formatted Sales Invoice/fiscal document number. |
| `fiscalSeries` | Fiscal series/policy code used for numbering. |
| `fiscalNumberPrefixText` | Prefix used at assignment time. |
| `fiscalNumberSuffixText` | Suffix used at assignment time. |
| `fiscalNumberAssignedAt` | Assignment timestamp from database context. |
| `fiscalNumberAssignedByRef` | Current runtime value such as `pos-server:system`. |

### 10.10 Error Response Codes

Current create failure codes include:

| Code | HTTP status |
| --- | --- |
| `missing_payable_basis` | `400` |
| `missing_upstream_finality_reference` | `400` |
| `unapproved_discount_reference` | `400` |
| `sensitive_evidence_payload_not_allowed` | `400` |
| `unsupported_fiscal_document_request` | `400` |
| `missing_fiscal_tender` | `400` |
| `invalid_fiscal_tender` | `400` |
| `sensitive_tender_payload_not_allowed` | `400` |
| `invalid_fiscal_tax_detail` | `400` |
| `sensitive_tax_detail_payload_not_allowed` | `400` |
| `invalid_fiscal_discount_privilege_detail` | `400` |
| `sensitive_discount_privilege_payload_not_allowed` | `400` |
| `invalid_fiscal_total` | `400` |
| `sensitive_total_payload_not_allowed` | `400` |
| `fiscal_document_idempotency_conflict` | `409` |
| `fiscal_identity_not_found` | `400` |
| `fiscal_identity_ambiguous` | `400` |
| `fiscal_identity_not_effective` | `400` |
| `fiscal_sequence_policy_not_found` | `400` |
| `fiscal_sequence_policy_ambiguous` | `400` |
| `fiscal_sequence_policy_not_effective` | `400` |
| `fiscal_sequence_state_not_found` | `400` |
| `fiscal_sequence_state_not_effective` | `400` |
| `fiscal_number_allocation_failed` | `400` |
| `fiscal_document_number_format_failed` | `400` |
| `persistence_not_configured` | `503` |
| `invalid_persistence_configuration` | `503` |
| `persistence_write_failed` | `503` |

### 10.11 What This Endpoint Does Not Do

`POST /v1/fiscal-documents/` does not:

- declare payment finality
- verify payment provider outcome
- approve statutory entitlement
- mutate Central PMS payable basis
- issue `ExitAuthorization`
- open gates
- activate continuity
- approve manual release
- generate Digital SI URL
- render printable Sales Invoice output
- generate QR code
- generate X-read or Z-read
- generate BIR Sales Summary or Annex E reports
- generate Electronic Journal records at runtime
- generate POSLog/export output at runtime
- process reprints
- process void/refund/cancel/return fiscal adjustments
- execute reset counter, Z-counter, Grand Total Amount, failover, or recovery workflows

## 11. Fiscal Document Read Endpoint

### 11.1 Endpoint

| Field | Value |
| --- | --- |
| Method | `GET` |
| Route | `/v1/fiscal-documents/{fiscalDocumentId}` |
| Success HTTP status | `200 OK` |
| Not found HTTP status | `404 Not Found` |
| Persistence/configuration failure HTTP status | `503 Service Unavailable` |

### 11.2 Path Parameter

| Parameter | Type | Notes |
| --- | --- | --- |
| `fiscalDocumentId` | UUID | POS Server fiscal document id. |

### 11.3 Successful Response

Current `document` fields include:

- `fiscalDocumentId`
- `sitePosServerId`
- `channelTerminalId`
- `fiscalIdentityId`
- `fiscalDocumentTypeCodeId`
- `fiscalDocumentStatusCodeId`
- `fiscalSequencePolicyId`
- `fiscalSequenceValue`
- `fiscalDocumentNumber`
- `fiscalSeries`
- `fiscalNumberPrefixText`
- `fiscalNumberSuffixText`
- `fiscalNumberAssignedAt`
- `fiscalNumberAssignedByRef`
- `centralPmsParkingSessionRef`
- `centralPmsPaymentAttemptRef`
- `centralPmsPaymentConfirmationRef`
- `paymentFinalityRef`
- `vendorAckRef`
- `businessDayDate`
- `documentContextJson`
- `isActive`
- `createdAt`
- `updatedAt`
- `statusHistory`
- `documentLinks`
- `lines`
- `tenders`
- `taxDetails`
- `discountPrivilegeDetails`
- `totals`

### 11.4 GET Meaning

`GET` returns POS Server persisted fiscal document facts and numbering fields.

`GET` does not:

- create payment finality
- prove provider settlement
- record Central PMS fiscal reference
- issue `ExitAuthorization`
- authorize gate opening
- approve statutory entitlement
- approve manual release
- activate continuity

Central PMS should use `GET` as fiscal document readback/reconciliation support, not as payment authority.

## 12. Fiscal Numbering Fields

Current runtime create response and read model expose:

| Field | Current behavior |
| --- | --- |
| `fiscalIdentityId` | Resolved server-side from active Site POS Server fiscal identity relationship. |
| `fiscalSequencePolicyId` | Resolved server-side from active Site POS Server/document type policy. |
| `fiscalSequenceValue` | Allocated server-side from locked sequence state. |
| `fiscalDocumentNumber` | Formatted server-side from policy prefix, sequence value, and suffix. |
| `fiscalSeries` | Current policy code copied at assignment time. |
| `fiscalNumberPrefixText` | Policy prefix copied at assignment time. |
| `fiscalNumberSuffixText` | Policy suffix copied at assignment time. |
| `fiscalNumberAssignedAt` | Database timestamp used during allocation. |
| `fiscalNumberAssignedByRef` | Current runtime assignment actor reference, e.g. `pos-server:system`. |

Central PMS may depend on these fields being present in successful current create responses and persisted GET readbacks when runtime fiscal issuance succeeds.

## 13. Central PMS Integration Contract

Central PMS shall:

- call `POST /v1/fiscal-documents/` only after verified platform payment finality
- provide approved payable-basis reference and upstream finality reference
- provide Central PMS payment/session references as reference values only
- preserve the returned fiscal document id and fiscal numbering fields as fiscal issuance evidence
- record the fiscal issuance reference in Central PMS
- withhold normal `ExitAuthorization` until fiscal issuance succeeds and Central PMS records the fiscal reference, unless a separately approved manual/exception policy applies
- retry uncertain network outcomes using the same upstream finality reference and same semantic request body
- treat `409` idempotency conflict as fail-closed and investigate

Central PMS must not treat POS Server response as:

- provider payment confirmation
- platform payment finality creation
- exit authorization
- gate instruction
- discount entitlement approval
- continuity activation
- manual release approval

## 14. Payment Channel and Terminal Posture

Payment channels and terminals are not independent POS systems.

Supported v1.3 terminology:

- WebPay
- APM
- Cashier-Assisted Terminal
- Continuity Terminal
- operator-assisted payment if later approved
- future channels

Channels/terminals may display, print, or present fiscal information only according to approved downstream design. They do not issue fiscal documents independently.

## 15. Deferred API Surface

The following are not currently runtime-supported API contracts and must not be treated as implemented:

- Digital SI URL creation
- printable Sales Invoice rendering
- QR generation/presentation contract
- X-read
- Z-read
- BIR Sales Summary / Annex E-1
- Annex E-2 to E-5
- Electronic Journal runtime generation
- POSLog generation/export
- fiscal export packaging
- reprints
- void/refund/cancel/return fiscal adjustment APIs
- reset counter runtime mechanics
- Z-counter runtime mechanics
- Grand Total Amount runtime mechanics
- fiscal recovery/failover runtime behavior
- statutory discount entitlement validation
- payment finality ownership
- `ExitAuthorization`
- gate opening
- continuity activation
- manual release approval
- Operator Console fiscal mutation
- Management Dashboard fiscal mutation

Future API design may define placeholders for these areas, but this v1.0 runtime-aligned contract does not create endpoint contracts for them.

## 16. Open Questions and Deferred Decisions

| ID | Open question / deferred decision |
| --- | --- |
| API-OQ-001 | Final service authentication and authorization model. |
| API-OQ-002 | Whether a future external `Idempotency-Key` header should be added or whether upstream finality reference remains the canonical key. |
| API-OQ-003 | Exact response distinction between newly created and replayed fiscal document results. Runtime currently uses code `accepted` for both and differentiates by message. |
| API-OQ-004 | Final durable post-commit sequence gap, recovery, and audit policy. |
| API-OQ-005 | Final timeout, unknown completion, and retry status endpoint behavior. |
| API-OQ-006 | Central PMS fiscal reference recording callback or reconciliation endpoint, if any. |
| API-OQ-007 | Digital SI URL API and access security model. |
| API-OQ-008 | Printable Sales Invoice render/download API. |
| API-OQ-009 | X-read, Z-read, BIR Sales Summary, Annex E, EJ, POSLog, export, reprint, adjustment, reset, and recovery API contracts. |
| API-OQ-010 | Final error envelope standard across all POS Server APIs. |
| API-OQ-011 | Final API versioning and backward compatibility policy. |

## 17. Alignment Notes Against Current Runtime

Confirmed current runtime behavior:

- `POST /v1/fiscal-documents/` exists.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` exists.
- POST creates fiscal document header and child fiscal facts transactionally.
- POST computes deterministic semantic request hash server-side.
- POST uses idempotency scope/key derived from Site POS Server id, fiscal document type id, and upstream finality reference.
- POST inserts or locks `pos.idempotency_records`.
- Same key/same hash replays original fiscal document result.
- Same key/different hash fails as `fiscal_document_idempotency_conflict` with HTTP `409`.
- POST resolves fiscal identity server-side.
- POST resolves fiscal sequence policy server-side.
- POST locks selected `pos.fiscal_sequence_states` row with `FOR UPDATE`.
- POST allocates fiscal sequence value.
- POST formats and persists fiscal document number fields.
- POST response includes fiscal numbering fields after durable commit.
- GET returns persisted fiscal numbering fields.
- POS Server does not declare payment finality.
- POS Server does not issue `ExitAuthorization`.
- POS Server does not open gates.
- POS Server does not approve entitlement.
- POS Server does not activate continuity.
- POS Server does not approve manual release.

Runtime-specific caveat:

- Current POST success and replay both return code `accepted` and HTTP `202 Accepted`; replay is differentiated by message, not a distinct response code.

## 18. Requirements Traceability Summary

| Requirement area | Current contract section |
| --- | --- |
| Authority boundary | Sections 4, 13, 14 |
| Implemented endpoints | Sections 2, 10, 11 |
| Idempotency | Sections 8, 9, 10 |
| Sequence allocation | Sections 10, 12 |
| Fiscal identity/policy resolution | Section 10 |
| Central PMS integration | Section 13 |
| Deferred API features | Section 15 |
| Open questions | Section 16 |

## Appendix A: Acronyms

| Acronym | Meaning |
| --- | --- |
| API | Application Programming Interface |
| APM | AutoPay Machine |
| BIR | Bureau of Internal Revenue |
| EJ | Electronic Journal |
| POS | Point of Sale |
| SI | Sales Invoice |
| UUID | Universally Unique Identifier |

## Appendix B: Current Runtime Files Used

- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentEndpointRouteBuilderExtensions.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentCreationEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentReadEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentRequest.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/GetFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentCreationService.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalIssuanceIdempotencyResolver.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentSemanticRequestHasher.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/FiscalDocumentDraft.cs`
- `src/ExitPass.PosServer.Runtime/FiscalDocuments/IFiscalDocumentRepository.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentRepository.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentSql.cs`
- `src/ExitPass.PosServer.Persistence.Postgres/FiscalDocuments/PostgresFiscalDocumentReader.cs`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
