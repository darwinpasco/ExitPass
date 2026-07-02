# ExitPass Central PMS to POS Server Fiscal Issuance Integration Contract v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass Central PMS to POS Server Fiscal Issuance Integration Contract |
| Version | v1.0 |
| Product scope | ExitPass v1.3 |
| Status | Runtime-aligned integration addendum |
| Output format | Markdown only |
| Runtime baseline inspected | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |
| Primary API dependency | `POST /v1/fiscal-documents/`, `GET /v1/fiscal-documents/{fiscalDocumentId}` |
| Parent API contract | `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md` |

This addendum is approved for documentation alignment and integration planning. It does not define source code, SQL, migrations, DTO implementation classes, generated artifacts, deployment scripts, UAT scripts, or runbook procedures.

## 2. Purpose and Scope

This addendum defines how Central PMS shall integrate with POS Server for fiscal issuance after Central PMS has verified platform payment finality and prepared the approved payable basis.

It covers:

- Central PMS preconditions before fiscal issuance.
- Central PMS request construction responsibilities.
- POS Server fiscal issuance responsibilities.
- Idempotency and semantic request hash responsibilities.
- Handling `202 accepted` with `resultClassification = newly_created`.
- Handling `202 accepted` with `resultClassification = idempotent_replay`.
- Handling `409 fiscal_document_idempotency_conflict`.
- Handling `400` fail-closed request, fiscal validation, and configuration errors.
- Handling `503` persistence, service, and fiscal numbering evidence failures.
- Handling `errorPosture`.
- Fiscal reference recording rules.
- ExitAuthorization gating rules.
- Retry and reconciliation rules.
- GET readback and reconciliation behavior.
- Operator/manual review escalation points.
- Logging, audit, and correlation requirements.

This addendum does not create endpoint contracts for deferred POS Server features.

## 3. Authority Boundaries

Central PMS owns:

- platform payment finality
- payment-linked platform control state
- payable-basis readiness before fiscal issuance
- fiscal issuance reference recording
- normal ExitAuthorization
- degraded resolve decisions under approved policy
- reconciliation coordination for fiscal issuance outcomes

POS Server owns:

- resolved Site fiscal issuance
- Sales Invoice fiscal document creation in current runtime scope
- server-side fiscal identity resolution
- server-side fiscal sequence policy resolution
- fiscal sequence allocation
- fiscal document number formatting
- persisted fiscal document readback
- idempotent fiscal document creation replay/conflict behavior

POS Server does not:

- declare platform payment finality
- verify payment provider outcome as platform authority
- approve statutory entitlement
- mutate Central PMS payable basis directly
- record the Central PMS fiscal reference on behalf of Central PMS
- issue `ExitAuthorization`
- open gates
- activate continuity
- approve manual release
- operate as Operator Console, Management Dashboard, Assisted Payment Terminal, WebPay, APM, or Central PMS

Channels and terminals are not independent POS systems. WebPay, APM, Cashier-Assisted Terminal, Continuity Terminal, and future channels route fiscal issuance through Central PMS to the resolved Site POS Server.

## 4. Integration Sequence Overview

The normal fiscal issuance sequence is:

1. Central PMS resolves the Site and Site POS Server context.
2. Central PMS verifies platform payment finality.
3. Central PMS confirms payable basis and fiscal facts are stable.
4. Central PMS calls `POST /v1/fiscal-documents/` with a stable `payableBasis.upstreamFinalityRef`.
5. POS Server validates the fiscal request, idempotency key, semantic request hash, fiscal identity, fiscal sequence policy, and fiscal sequence state.
6. POS Server allocates fiscal number and persists fiscal document facts, or replays a matching idempotent result.
7. POS Server returns fiscal issuance evidence or a fail-closed response.
8. Central PMS records returned fiscal issuance reference evidence when the response is successful and complete.
9. Central PMS may proceed toward normal ExitAuthorization only after fiscal reference recording succeeds and all other eligibility rules are satisfied.

POS Server response is fiscal issuance evidence only. It is not payment finality and not a gate command.

## 5. Central PMS Preconditions Before Fiscal Issuance

Central PMS may call `POST /v1/fiscal-documents/` only after:

- Site is resolved.
- Site POS Server context is determined.
- parking session/payment context is known.
- payment finality is verified by Central PMS.
- payable basis is approved and stable.
- statutory discount validation has been completed by Central PMS / Discount workflow where applicable.
- payable basis and fiscal facts are ready.
- Central PMS has a stable upstream finality reference for idempotency.
- Central PMS is ready to record returned fiscal issuance evidence.

Central PMS must not call POS Server speculatively before payment finality and payable-basis readiness.

## 6. POS Server Responsibilities

For the current runtime fiscal issuance API, POS Server shall:

- accept fiscal document create requests for the resolved Site POS Server context.
- derive idempotency from `payableBasis.upstreamFinalityRef`.
- compute a deterministic semantic request hash server-side.
- replay same-key/same-hash requests safely.
- fail closed on same-key/different-hash conflicts.
- resolve fiscal identity server-side.
- resolve fiscal sequence policy server-side.
- lock the selected fiscal sequence-state row transactionally.
- allocate the fiscal sequence value.
- format and persist fiscal document number fields.
- persist fiscal document shell and child fiscal facts transactionally.
- return fiscal document identity and fiscal numbering fields after durable commit.
- expose fiscal evidence/status fields in create and read responses.
- fail closed if complete fiscal numbering evidence is not available.

POS Server shall not decide whether Central PMS may issue ExitAuthorization.

## 7. Central PMS Request Construction Responsibilities

Central PMS must provide stable and consistent values for:

| Field | Central PMS responsibility |
| --- | --- |
| `sitePosServerId` | Provide the resolved Site POS Server identifier. |
| `sitePosServerRef` | Provide the stable Site POS Server reference used by POS Server local fiscal context. |
| `fiscalDocumentTypeCodeId` | Provide the fiscal document type code id used for fiscal identity/policy resolution and idempotency scope. |
| `fiscalDocumentTypeCodeKey` | Provide the fiscal document type key. |
| `fiscalDocumentStatusCodeId` | Provide the initial fiscal document status code id expected by runtime validation. |
| `businessDayDate` | Provide the business day context where available. |
| `centralPmsParkingSessionRef` | Provide Central PMS parking session reference for traceability. |
| `centralPmsPaymentAttemptRef` | Provide Central PMS payment attempt reference for traceability. |
| `centralPmsPaymentConfirmationRef` | Provide Central PMS payment confirmation reference for traceability. |
| `payableBasis` | Provide approved payable basis object. |
| `payableBasis.payableBasisRef` | Provide stable approved payable-basis reference. |
| `payableBasis.upstreamFinalityRef` | Provide stable idempotency key source for the fiscal issuance attempt. |
| `payableBasis.currencyCode` | Provide currency code consistent with the approved payable basis. |
| `payableBasis.payableAmountMinorUnits` | Provide payable amount in minor units. |
| approved discount references | Provide approved Central PMS / Discount workflow references when statutory discount treatment applies. |
| document lines | Provide fiscal document lines derived from approved payable/fiscal facts. |
| tenders | Provide tender facts linked to Central PMS payment context. |
| tax details | Provide approved fiscal tax details where available. |
| totals | Provide fiscal totals consistent with the request. |
| reference context | Provide traceability references only, not raw sensitive payloads. |

Central PMS must not send:

- raw sensitive evidence
- raw provider callback payloads
- card PAN/CVV
- secrets
- tokens
- credentials
- uncontrolled raw payment payloads
- raw statutory entitlement evidence where only evidence references are allowed

If the fiscal request contains statutory discount treatment, Central PMS must include approved discount validation references. POS Server does not approve entitlement.

## 8. Idempotency and Semantic Request Hash Responsibilities

Current POS Server idempotency behavior:

| Element | Runtime behavior |
| --- | --- |
| Idempotency scope | Fiscal document creation operation + Site POS Server id + fiscal document type code id. |
| Idempotency key | `payableBasis.upstreamFinalityRef`. |
| Semantic request hash | Server-computed deterministic hash over normalized fiscal request facts. |

Central PMS shall:

- keep the same `payableBasis.upstreamFinalityRef` for the same fiscal issuance attempt.
- retry uncertain network outcomes with the same semantic request body and same upstream finality reference.
- not reuse an upstream finality reference for a semantically different issuance.
- treat same key + same semantic request hash as safe replay.
- treat same key + different semantic request hash as conflict/fail closed.
- preserve idempotency context in audit and reconciliation records.

Replay does not allocate a new fiscal number. Conflict does not allocate a fiscal number.

Central PMS must not call POS Server with a new upstream finality reference solely to bypass an idempotency conflict unless a formal correction/supervised process exists.

## 9. Handling `202 accepted` with `resultClassification = newly_created`

First-time successful fiscal issuance has the following current runtime posture:

| Response element | Expected value / meaning |
| --- | --- |
| HTTP status | `202 Accepted` |
| `code` | `accepted` |
| `resultClassification` | `newly_created` |
| `fiscalIssuanceEvidenceStatus` | `fiscal_document_number_assigned` |
| `fiscalNumberAssignmentState` | `assigned` |
| `fiscalDocumentId` | POS Server fiscal document id. |
| `fiscalIdentityId` | Resolved fiscal identity id. |
| `fiscalSequencePolicyId` | Resolved fiscal sequence policy id. |
| `fiscalSequenceValue` | Allocated sequence value. |
| `fiscalDocumentNumber` | Assigned Sales Invoice/fiscal document number. |
| `fiscalSeries` | Fiscal series at assignment time. |
| `fiscalNumberPrefixText` | Prefix at assignment time. |
| `fiscalNumberSuffixText` | Suffix at assignment time. |
| `fiscalNumberAssignedAt` | Assignment timestamp. |
| `fiscalNumberAssignedByRef` | Assignment actor reference. |
| `fiscalDocumentStatusCodeId` | Fiscal document status code id persisted on the document. |

Central PMS shall:

- record fiscal document id and fiscal numbering fields as fiscal issuance reference evidence.
- record POS Server response status and correlation id if available.
- record `resultClassification = newly_created`.
- record `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`.
- record `fiscalNumberAssignmentState = assigned`.
- proceed toward normal ExitAuthorization only after successful fiscal reference recording.
- preserve traceability to payment confirmation, parking session, Site, and upstream finality reference.

Central PMS must not treat POS Server success as payment finality, because payment finality already belongs to Central PMS. Central PMS must not treat POS Server success as a gate command.

## 10. Handling `202 accepted` with `resultClassification = idempotent_replay`

Same-key/same-hash replay has the following current runtime posture:

| Response element | Expected value / meaning |
| --- | --- |
| HTTP status | `202 Accepted` |
| `code` | `accepted` |
| `resultClassification` | `idempotent_replay` |
| `fiscalIssuanceEvidenceStatus` | `fiscal_document_number_assigned` when replay readback has complete fiscal numbering evidence. |
| `fiscalNumberAssignmentState` | `assigned` when replay readback has complete fiscal numbering evidence. |
| `fiscalDocumentId` | Original POS Server fiscal document id. |
| fiscal numbering fields | Original fiscal numbering fields. |

Replay does not advance sequence state and does not allocate another fiscal number.

Central PMS shall:

- treat replay as successful fiscal issuance evidence only when `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned` and `fiscalNumberAssignmentState = assigned`.
- record the fiscal reference if none exists yet.
- reconcile against the existing Central PMS fiscal reference if one already exists.
- not create duplicate Central PMS fiscal references for the same fiscal issuance.
- detect mismatch if returned fiscal document id or fiscal document number differs from an already-recorded Central PMS fiscal reference.
- escalate mismatches to operator/manual review and reconciliation.
- not issue duplicate ExitAuthorization.

## 11. Handling `409 fiscal_document_idempotency_conflict`

`409 fiscal_document_idempotency_conflict` means the same upstream finality reference was used with a semantically different request.

Central PMS shall:

- fail closed.
- not retry automatically with the same key and changed payload.
- not call with a new key just to bypass the conflict unless a formal correction/supervised process exists.
- not record fiscal issuance complete.
- not issue normal ExitAuthorization.
- record the conflict event.
- route the case to operator/manual review and reconciliation.

Conflict response must be treated as evidence of unsafe duplicate or changed fiscal issuance intent, not as a transient retry condition.

## 12. Handling `400` Fail-Closed Fiscal Validation/Configuration Errors

Current `400` failures include request correction errors and fiscal setup/configuration errors.

Request correction required:

- `missing_payable_basis`
- `missing_upstream_finality_reference`
- `unapproved_discount_reference`
- `unsupported_fiscal_document_request`
- `invalid_fiscal_tender`
- `missing_fiscal_tender`
- `invalid_fiscal_tax_detail`
- `invalid_fiscal_discount_privilege_detail`
- `invalid_fiscal_total`
- sensitive payload rejection codes

Configuration/fiscal setup correction required:

- `fiscal_identity_not_found`
- `fiscal_identity_ambiguous`
- `fiscal_identity_not_effective`
- `fiscal_sequence_policy_not_found`
- `fiscal_sequence_policy_ambiguous`
- `fiscal_sequence_policy_not_effective`
- `fiscal_sequence_state_not_found`
- `fiscal_sequence_state_not_effective`
- `fiscal_number_allocation_failed`
- `fiscal_document_number_format_failed`

Central PMS shall:

- fail closed.
- not issue normal ExitAuthorization.
- not mark fiscal issuance complete.
- correct request/configuration before retry according to `errorPosture`.
- preserve failed attempt and audit details.
- route to operations/manual review when required.

Central PMS must not treat a `400` response as evidence that the payment failed. Payment finality remains a Central PMS record.

## 13. Handling `503` Persistence/Service/Fiscal Numbering Evidence Failures

Current `503` failure cases include:

- `persistence_not_configured`
- `invalid_persistence_configuration`
- `persistence_write_failed`
- `fiscal_number_assignment_incomplete`
- service unavailable/recovery-required conditions

Central PMS shall:

- not record fiscal issuance evidence unless a later safe readback confirms a numbered fiscal document.
- not issue normal ExitAuthorization.
- preserve the same upstream finality reference for retry if request semantics are unchanged.
- perform GET readback if `fiscalDocumentId` is available and readback is safe.
- retry only after service recovery or operator-confirmed recovery path.
- escalate if outcome is unknown.
- not issue a new fiscal request with a different upstream finality reference for the same payment finality unless an approved correction process exists.

For `fiscal_number_assignment_incomplete`, Central PMS must not record fiscal issuance evidence from the failed POST response. If a `fiscalDocumentId` is returned, Central PMS may use GET readback during controlled reconciliation to determine whether complete persisted fiscal numbering evidence exists.

## 14. Handling `errorPosture`

`errorPosture` is conservative guidance from POS Server. It is not a full retry scheduler.

| `errorPosture` | Central PMS interpretation |
| --- | --- |
| `do_not_retry_without_request_change` | The request is semantically invalid, conflicting, unsupported, or unsafe. Central PMS must correct the request or investigate before retry. |
| `retry_after_configuration_correction` | Fiscal configuration/state is missing, ambiguous, inactive, unsafe, or not effective. Operator/configuration correction is required before retry. |
| `retry_after_service_recovery` | Persistence, service configuration, availability, or fiscal numbering completeness problem. Retry only after service recovery or operational investigation. |

Central PMS retry timing, retry limits, backoff, scheduler ownership, and escalation automation remain implementation decisions for a later engineering task.

## 15. Fiscal Reference Recording Rules

When fiscal issuance evidence is complete, Central PMS should durably record at least:

- POS Server fiscal document id
- fiscal identity id
- fiscal sequence policy id
- fiscal sequence value
- fiscal document number
- fiscal series
- prefix/suffix at assignment time
- fiscal number assigned at
- fiscal number assigned by ref
- fiscal document status code id
- result classification
- fiscal issuance evidence status
- fiscal number assignment state
- upstream finality reference
- Central PMS payment confirmation ref
- Central PMS payment attempt ref
- Central PMS parking session ref
- request hash or correlation reference if available
- POS Server response timestamp
- retry/replay/conflict history

If existing Central PMS schema does not yet support all fields, this is an implementation gap for the later API/database/engineering pack. The gap must not be solved by dropping fiscal traceability requirements from the integration contract.

Central PMS must record fiscal issuance reference durably before issuing normal ExitAuthorization.

## 16. ExitAuthorization Gating Rules

Normal ExitAuthorization must not be issued until:

1. Central PMS has verified payment finality.
2. Central PMS has successfully called POS Server or replayed successful fiscal issuance.
3. POS Server returned `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`.
4. POS Server returned `fiscalNumberAssignmentState = assigned`.
5. Central PMS durably recorded fiscal issuance reference.

If fiscal issuance fails, is unknown, or cannot be verified, Central PMS must not issue normal ExitAuthorization.

Exception/manual release policy must be separately approved, auditable, incident-tagged, and reconciliation-tagged. Manual release is not normal ExitAuthorization.

POS Server never issues ExitAuthorization.

## 17. Retry and Reconciliation Rules

Central PMS retry posture:

- Retry uncertain network outcomes with the same upstream finality reference and same semantic request body.
- Treat successful idempotent replay as fiscal issuance evidence only when fiscal evidence and assignment status are complete.
- Do not mutate request semantics under the same upstream finality reference.
- Do not use a different upstream finality reference to bypass a conflict.
- Preserve retry count, timestamps, response codes, `errorPosture`, and correlation references.
- Escalate unknown or contradictory outcomes to operator/manual review.

Reconciliation posture:

- Reconcile Central PMS fiscal reference records to POS Server fiscal document id and fiscal document number.
- Reconcile replay responses against existing Central PMS fiscal reference.
- Flag mismatches between Central PMS and POS Server fiscal identity/numbering fields.
- Track unresolved fiscal issuance cases separately from payment finality.
- Keep fiscal issuance exceptions visible to Operator Console and Management Dashboard where authorized.

This addendum does not define final retry scheduler implementation, queue names, retry counts, or timeout values.

## 18. GET Readback/Reconciliation Behavior

Central PMS should call `GET /v1/fiscal-documents/{fiscalDocumentId}`:

- after uncertain POST outcome when fiscal document id is known.
- after `503` with `fiscalDocumentId` if available and readback is safe.
- during reconciliation.
- to verify existing fiscal reference.
- to resolve duplicate/replay ambiguity.
- during operator review.

GET response means:

- POS Server persisted fiscal document readback.
- persisted fiscal numbering evidence where `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned` and `fiscalNumberAssignmentState = assigned`.

GET response does not mean:

- payment finality
- ExitAuthorization
- gate command
- BIR report finality
- Digital SI finality
- entitlement approval
- manual release approval
- continuity activation

If GET returns `fiscalNumberAssignmentState = not_assigned`, Central PMS must not record normal fiscal issuance evidence without additional approved reconciliation handling.

## 19. Operator/Manual Review Escalation Points

Central PMS should escalate to operator/manual review when:

- `409 fiscal_document_idempotency_conflict` occurs.
- fiscal issuance fails with `do_not_retry_without_request_change`.
- fiscal configuration/state requires correction.
- service recovery is required and the customer/vehicle status is operationally sensitive.
- POST outcome is unknown and GET readback is unavailable or inconclusive.
- replay response conflicts with an already-recorded Central PMS fiscal reference.
- fiscal document id or fiscal document number mismatches are detected.
- fiscal issuance remains pending beyond approved operational tolerance.
- manual release exception is requested.

Operator Console may support governance/review. Operator Console must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.

## 20. Logging/Audit/Correlation Requirements

Central PMS should capture and correlate:

- Central PMS request id
- POS Server fiscal document id
- upstream finality reference
- payment confirmation ref
- payment attempt ref
- parking session ref
- Site id / Site POS Server id
- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- `fiscalDocumentNumber`
- fiscal sequence value
- fiscal identity id
- fiscal sequence policy id
- fiscal document status code id
- error code
- `errorPosture`
- retry attempt number
- operator/manual review id if applicable
- request/response timestamp
- correlation id if available

Audit records must support reconstruction from payment finality to fiscal issuance reference to ExitAuthorization decision.

## 21. What Central PMS Must Not Infer

Central PMS must not infer from POS Server create/read responses:

- payment finality
- payment provider settlement
- vendor payment acknowledgment
- ExitAuthorization
- gate permission
- statutory entitlement approval
- payable-basis mutation
- manual release approval
- continuity activation
- BIR report finality
- X-read/Z-read finality
- Annex E finality
- Electronic Journal export completion
- POSLog export completion
- Digital SI issuance
- fiscal recovery completion

POS Server fiscal issuance evidence is necessary for normal ExitAuthorization gating, but it is not sufficient by itself.

## 22. Deferred Items

This addendum does not define implemented endpoint contracts for:

- Digital SI
- printable SI rendering
- QR presentation
- X-read
- Z-read
- BIR Sales Summary
- Annex E
- Electronic Journal
- POSLog
- reprints
- void/refund/cancel/return fiscal adjustments
- reset counter mechanics
- Z-counter mechanics
- Grand Total Amount mechanics
- recovery automation
- gate opening
- ExitAuthorization endpoint
- continuity activation
- manual release approval

These remain deferred until the corresponding runtime/API designs exist.

## 23. Open Questions

| ID | Open question / deferred decision |
| --- | --- |
| CPOS-OQ-001 | Final service-to-service authentication and authorization model between Central PMS and POS Server. |
| CPOS-OQ-002 | Whether a future external `Idempotency-Key` header should be introduced or `payableBasis.upstreamFinalityRef` remains canonical. |
| CPOS-OQ-003 | Final Central PMS schema/API/database support for all fiscal reference recording fields listed in this addendum. |
| CPOS-OQ-004 | Final retry scheduler ownership, retry counts, timeout values, and backoff behavior. |
| CPOS-OQ-005 | Final unknown-outcome handling when POST times out before a fiscal document id is known. |
| CPOS-OQ-006 | Final durable post-commit sequence gap and recovery policy. |
| CPOS-OQ-007 | Final operator/manual review workflow and permission matrix for fiscal issuance exceptions. |
| CPOS-OQ-008 | Final Management Dashboard and Operator Console visibility fields for fiscal exception queues. |
| CPOS-OQ-009 | Final cross-API error envelope standard across Central PMS and POS Server. |
| CPOS-OQ-010 | Final implementation events, queues, and reconciliation job ownership, deferred to engineering design. |

## 24. Requirements Traceability Summary

| Requirement area | Source / target |
| --- | --- |
| Fiscal issuance before ExitAuthorization | ExitPass BRD v1.3; ExitPass System Design v1.3; POS/Invoicing BRD v1.0; Sections 4, 16 |
| Central PMS payment finality authority | ExitPass System Design v1.3; Sections 3, 5, 13, 21 |
| POS Server fiscal authority | POS Server System Design v1.0; POS Server API Contract v1.0; Sections 3, 6 |
| Idempotency and semantic hash | POS Server API Contract v1.0; runtime idempotency slice; Sections 8, 10, 11, 17 |
| Fiscal identity/policy/sequence allocation | Runtime policy/identity and sequence allocation slices; Sections 6, 9, 15 |
| Response/status hardening | Runtime response/status hardening slice; Sections 9, 10, 13, 14, 18 |
| Fiscal reference recording | ExitPass System Design v1.3; POS/Invoicing BRD v1.0; Sections 15, 20 |
| GET readback/reconciliation | POS Server API Contract v1.0; Sections 18, 19 |
| Deferred API surface | POS Server API Contract v1.0; Section 22 |

## Appendix A: Acronyms

| Acronym | Meaning |
| --- | --- |
| API | Application Programming Interface |
| APM | AutoPay Machine |
| BIR | Bureau of Internal Revenue |
| DTO | Data Transfer Object |
| PMS | Parking Management System |
| POS | Point of Sale |
| SI | Sales Invoice |

## Appendix B: Referenced Runtime Files

- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentRequest.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/CreateFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/GetFiscalDocumentResponse.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentCreationEndpoint.cs`
- `src/ExitPass.PosServer.Api/FiscalDocuments/FiscalDocumentReadEndpoint.cs`
- `tests/ExitPass.PosServer.Api.Tests/FiscalDocumentCreationEndpointTests.cs`
- `tests/ExitPass.PosServer.Api.Tests/FiscalDocumentReadEndpointTests.cs`
- `tests/ExitPass.PosServer.Api.IntegrationTests/FiscalDocumentApiPostgresSmokeTests.cs`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`
