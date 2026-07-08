# ExitPass Operator Console Fiscal Issuance Status Visibility Contract v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Operator Console Fiscal Issuance Status Visibility Contract |
| Version | v1.0 |
| Date | 2026-07-08 |
| Branch | `docs/operator-console-fiscal-issuance-status-visibility-contract` |
| Scope | Operator Console, support, and audit display contract for read-only fiscal issuance status |
| Source endpoint | `GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}` |
| Required permission | `FiscalIssuanceStatusRead` |

This is a documentation-only contract. It does not implement source code, schema, tests, runtime configuration, retry behavior, fiscal issuance mutation, payment confirmation, ExitAuthorization, gate behavior, or UAT execution.

## 2. Purpose And Scope

This contract defines how Operator Console, support, and audit surfaces may consume and display Central PMS fiscal issuance status safely.

The status surface is intended for internal operational visibility after Central PMS has recorded fiscal issuance state. It helps authorized users understand whether fiscal issuance was recorded, replayed, conflicted, failed due to service posture, or missing from the read model.

The display must preserve the ExitPass authority model:

- Central PMS remains the source for recorded fiscal issuance reference state exposed by this endpoint.
- POS Server remains the fiscal issuance and fiscal numbering authority.
- Payment finality remains separate from fiscal status visibility.
- ExitAuthorization and gate opening remain separate from fiscal status visibility.
- Operator Console is a visibility and governance surface, not a fiscal mutation surface.

Reference context:

- `docs/v1.3/central-pms/fiscal-issuance/ExitPass_Central_PMS_Fiscal_Issuance_Status_Read_API_Implementation_Note_v1.0.md`
- `docs/v1.3/central-pms/fiscal-issuance/ExitPass_Central_PMS_Fiscal_Issuance_Status_Read_API_Access_Policy_Implementation_Note_v1.0.md`
- `docs/v1.3/operator-console/diagrams/OC-D06_Fiscal_Status_Visibility_and_Exception_Handoff.puml`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_First_Run_Readiness_Refresh_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Fiscal_Issuance_Controlled_UAT_Runbook_v1.0.md`

## 3. Consumer Roles

| Role | Intended Use | Access Posture |
| --- | --- | --- |
| Site Operator | View whether a transaction has a recorded fiscal issuance status and identify when supervisor/support escalation is needed. | Read-only, site-scoped where supported by the caller's broader session/site authorization model. |
| Site Supervisor | Review fiscal issuance exceptions, replay posture, conflict posture, failed-service posture, and escalation notes for site operations. | Read-only, site or site-group scoped where supported by the caller's broader authorization model. |
| Compliance Auditor | Review fiscal issuance status, assigned Sales Invoice number, timestamp, state transitions, and support/audit references. | Read-only, audit-scoped, with minimized operational context and no mutation controls. |
| Administrator/support | Diagnose fiscal issuance visibility, correlate references, and support escalation for conflict or failed-service cases. | Read-only, support/audit detail access only when `FiscalIssuanceStatusRead` and operational authorization are present. |

These roles do not receive fiscal issuance retry authority from this contract. Any future retry, readback, writeback, closure, refund, reversal, or gate action must be defined in a separate implementation and approval slice.

## 4. Read-Only Boundary

Operator Console and related support/audit surfaces must treat the fiscal issuance status endpoint as read-only.

Allowed:

- Request recorded fiscal issuance status by known `fiscalIssuanceReferenceId`.
- Display safe state, number, reference, error posture, and audit timestamps according to this contract.
- Log that an authorized user viewed the fiscal issuance status.
- Link or hand off to a separately governed fiscal exception workflow when one exists.

Not allowed:

- Call POS Server directly.
- Trigger fiscal issuance.
- Trigger fiscal retry, readback, writeback, closure, refund, reversal, payment confirmation, ExitAuthorization, or gate opening.
- Convert fiscal status into payment status.
- Convert fiscal status into exit authorization status.
- Display raw payloads, secrets, stack traces, payment provider payloads, customer PII, or statutory evidence payloads.

## 5. Source Endpoint And Authorization

Source endpoint:

```text
GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}
```

Required Central PMS policy:

```text
FiscalIssuanceStatusRead
```

Expected endpoint access behavior:

| Condition | HTTP Status | Display Posture |
| --- | --- | --- |
| Caller unauthenticated | `401` | Do not show fiscal status. Show standard unauthorized access handling. |
| Caller lacks permission | `403` | Do not show fiscal status. Show access-denied handling. |
| Reference exists | `200` | Display fiscal issuance status according to this contract. |
| Reference missing | `404` with `FISCAL_ISSUANCE_REFERENCE_NOT_FOUND` | Show `Fiscal reference not found`; do not imply unpaid, unauthorized, voided, reversed, or not issued unless another authoritative source states that. |

## 6. Display Rules

Operator Console/support/audit surfaces must:

- Show fiscal issuance state from `fiscalIssuanceState`.
- Show Sales Invoice number only when `fiscalDocumentNumber` exists.
- Label `fiscalDocumentNumber` as the Sales Invoice number or fiscal document number, depending on the document type context.
- Show `posServerFiscalDocumentId` only as a support/audit reference, not as the customer-facing invoice number.
- Show replay, conflict, and failed-service posture clearly.
- Show safe error code and posture where available.
- Avoid unsupported fiscal or statutory final wording.

Operator Console/support/audit surfaces must not:

- Call the status `paid`.
- Call the status `authorized to exit`.
- Imply gate authorization.
- Imply the barrier should open.
- Render `FISCAL_ISSUANCE_REPLAYED` as a duplicate issuance.
- Render `FISCAL_ISSUANCE_CONFLICT` as user-retryable without request correction.
- Render `FISCAL_ISSUANCE_FAILED_SERVICE` as final fiscal failure without support review.

## 7. State Display Mapping

| API State / Condition | Primary Label | Required Display Meaning | Action Guidance |
| --- | --- | --- | --- |
| `FISCAL_ISSUANCE_RECORDED` with `fiscalDocumentNumber` present | Issued | Fiscal issuance was recorded and a Sales Invoice/fiscal document number is assigned. | Display number and timestamp. No retry prompt. |
| `FISCAL_ISSUANCE_RECORDED` without `fiscalDocumentNumber` | Recorded - number not available | Central PMS recorded fiscal issuance state, but the status response does not include an assigned number. | Show support/audit details; escalate if the number is expected. Do not label as `Issued`. |
| `FISCAL_ISSUANCE_REPLAYED` | Existing issuance reused | Same-key/same-hash replay reused the existing fiscal issuance record. | Show the existing Sales Invoice number when present. Do not imply duplicate issuance. |
| `FISCAL_ISSUANCE_CONFLICT` | Fiscal issuance conflict | Same-key/different-request posture or equivalent conflict; safe error details should guide escalation. | Instruct user to escalate to support/supervisor. Do not retry blindly. |
| `FISCAL_ISSUANCE_FAILED_SERVICE` | Fiscal service failed | Fiscal issuance did not complete because of service/runtime failure posture. | Instruct support review. Retry only through a separately approved retry workflow. |
| Missing reference / `404` | Fiscal reference not found | The requested fiscal issuance reference is not available through the status endpoint. | Verify reference/source context. Do not infer payment, exit, refund, reversal, or gate state. |

Additional states may exist in the endpoint response. Until a specific Operator Console display mapping is approved, unknown or unmapped states must be shown as `Fiscal status requires review` with the raw state visible only in support/audit detail.

## 8. Error Posture Display

| Field / Value | Display Meaning | Required UX Posture |
| --- | --- | --- |
| `latestErrorCode = fiscal_document_idempotency_conflict` | POS Server or Central PMS recorded an idempotency conflict posture for the fiscal document request. | Show `Fiscal issuance conflict`. Instruct escalation. Do not retry unless the request is corrected and an approved workflow exists. |
| `latestErrorPosture = DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE` | The same request key cannot be retried blindly because the request content/posture differs. | Show clear non-retry guidance: `Escalate for review. Do not retry without corrected request details.` |
| `FISCAL_ISSUANCE_FAILED_SERVICE` with service recovery posture | Service dependency or runtime path failed safely. | Show failed-service posture and support review instruction. Do not expose stack traces or raw provider/service payloads. |

Safe failed-service wording:

```text
Fiscal service failed. Support review is required before any retry or closure action.
```

Conflict wording:

```text
Fiscal issuance conflict. Escalate for review; do not retry without corrected request details.
```

Replay wording:

```text
Existing fiscal issuance reused. No duplicate issuance was created.
```

## 9. Fields Allowed For Display

These fields may be displayed in the main fiscal status panel when present and relevant:

| API Field | Display Use |
| --- | --- |
| `fiscalIssuanceState` | Primary fiscal status label and state badge. |
| `resultClassification` | Secondary label such as newly created or idempotent replay. |
| `fiscalIssuanceEvidenceStatus` | Fiscal evidence status indicator. |
| `fiscalNumberAssignmentState` | Number assignment status. |
| `fiscalDocumentNumber` | Sales Invoice/fiscal document number when assigned. |
| `fiscalDocumentTypeCodeKey` | Document type context when safe and useful. |
| `firstRecordedAt` | First recorded timestamp. |
| `lastUpdatedAt` | Last updated timestamp. |
| `latestErrorCode` | Safe error code for user/support posture. |
| `latestErrorPosture` | Safe escalation/retry posture. |
| `latestExceptionReason` | Safe exception category when present. |

`Issued` may be shown only when `fiscalDocumentNumber` exists. If `fiscalNumberAssignmentState = ASSIGNED` but `fiscalDocumentNumber` is absent, display a support-review posture instead of `Issued`.

## 10. Fields Allowed Only For Support/Audit Detail

These fields may be shown only in a collapsed support/audit detail section, audit export, or administrator/support diagnostic view protected by the same permission and any broader role/site scoping rules:

| API Field | Support/Audit Use |
| --- | --- |
| `fiscalIssuanceReferenceId` | Fiscal status lookup and cross-system correlation. |
| `upstreamFinalityReference` | Support correlation to upstream finality context; not a payment-status label. |
| `paymentConfirmationId` | Support correlation only; does not make this endpoint a payment confirmation surface. |
| `paymentAttemptId` | Support correlation only. |
| `parkingSessionId` | Support correlation only. |
| `siteId` | Site correlation only. |
| `sitePosServerId` | POS Server site mapping correlation only. |
| `sitePosServerRef` | POS Server site reference correlation only. |
| `fiscalDocumentTypeCodeId` | Fiscal setup correlation only. |
| `posServerFiscalDocumentId` | POS Server fiscal document reference for support/audit only. |
| `fiscalIdentityId` | Fiscal setup correlation only. |
| `fiscalSequencePolicyId` | Fiscal setup correlation only. |
| `fiscalSequenceValue` | Sequence audit context only. |
| `fiscalSeries` | Fiscal number context only. |
| `fiscalNumberPrefixText` | Fiscal number context only. |
| `fiscalNumberSuffixText` | Fiscal number context only. |
| `fiscalNumberAssignedAt` | Assignment audit timestamp. |
| `fiscalNumberAssignedByRef` | Assignment actor/system reference. |
| `semanticRequestHashValue` | Support/audit comparison only; do not expose to general operator view. |
| `semanticRequestHashVersion` | Support/audit hash metadata. |
| `semanticRequestHashStatus` | Support/audit hash readiness metadata. |
| `semanticRequestHashAlgorithm` | Support/audit hash metadata. |
| `semanticRequestHashSourceFactCount` | Support/audit hash metadata. |
| `correlationId` | Support/audit log correlation. |

Support/audit detail must not make these fields appear customer-facing, statutory-facing, or operationally authoritative beyond their recorded fiscal status context.

## 11. Fields Never Displayed

The Operator Console, support, and audit status surfaces must never display:

- raw request payloads
- secrets
- stack traces
- payment provider raw payloads
- customer PII
- statutory evidence payloads
- raw entitlement evidence
- raw POS Server request bodies
- raw payment callbacks
- local environment variables or credentials

The Central PMS status endpoint is already intended to exclude these values. UI and audit consumers must preserve that exclusion and must not enrich the view from unsafe logs or side channels.

## 12. Explicit Non-Goals

This contract does not define or authorize:

- payment confirmation
- ExitAuthorization
- gate opening
- fiscal retry
- fiscal readback
- fiscal writeback
- fiscal closure
- refund or reversal
- POS Server mutation
- direct POS Server calls from Operator Console
- PDF generation
- HTML generation
- QR generation
- BIR final statutory wording
- production certification
- UAT scenario execution

## 13. UX Notes

- Use `Issued` only when `fiscalDocumentNumber` exists.
- Show `FISCAL_ISSUANCE_REPLAYED` as existing issuance reused.
- Show replay details without implying duplicate fiscal number allocation.
- Show `FISCAL_ISSUANCE_CONFLICT` with escalation guidance, not a retry button.
- Show `FISCAL_ISSUANCE_FAILED_SERVICE` with support review guidance.
- Show missing reference as a lookup/reference problem, not as a payment, exit, or gate outcome.
- Keep `posServerFiscalDocumentId` visually subordinate to `fiscalDocumentNumber`.
- Do not use green success treatment for any state that lacks an assigned `fiscalDocumentNumber`.
- Do not use gate, exit, payment, or receipt-finality wording in the fiscal status panel.

Recommended labels:

| Condition | Recommended Label |
| --- | --- |
| Recorded with number | `Issued` |
| Recorded without number | `Recorded - number not available` |
| Replay | `Existing issuance reused` |
| Conflict | `Fiscal issuance conflict` |
| Failed service | `Fiscal service failed` |
| Missing reference | `Fiscal reference not found` |

## 14. Audit And Logging Expectations

Viewing fiscal issuance status must be auditable.

Each status view should record:

- authenticated user or service identity;
- role or permission context used for the view;
- site/site-group context where available;
- `fiscalIssuanceReferenceId`;
- timestamp;
- source module or screen;
- result class: success, not found, unauthorized, forbidden, or service error;
- correlation id for the UI/API request where available.

Audit logs must not include raw request payloads, secrets, stack traces, payment provider raw payloads, customer PII, statutory evidence payloads, or full unsafe downstream payloads.

Audit events for viewing are observational only. They must not mutate fiscal state, payment state, ExitAuthorization state, gate state, retry queues, readback queues, or exception closure state.

## 15. Recommended Next Implementation Slice

Recommended next slice:

```text
Implement Operator Console read-only fiscal issuance status viewer
```

Suggested scope:

- Add an Operator Console read-only fiscal status panel backed by `GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}`.
- Enforce `FiscalIssuanceStatusRead` through the existing internal authorization path.
- Implement the display/state/error mapping in this contract.
- Add view-audit logging for status reads.
- Keep support/audit detail collapsed and role/permission guarded.
- Add UI tests for recorded, replayed, conflict, failed-service, missing-reference, unauthorized, and forbidden display behavior.

Out of scope for that slice:

- fiscal retry;
- fiscal readback/writeback;
- FEQ mutation;
- payment confirmation;
- ExitAuthorization;
- gate opening;
- refund/reversal;
- PDF/HTML/QR generation;
- final BIR statutory wording.

The implementation slice should continue to use the readiness checkpoint and controlled UAT runbook only as reference material. It must not execute UAT scenarios as part of building the visibility panel.

## 16. Completion Checklist

| Requirement | Status |
| --- | --- |
| Purpose and scope defined | Covered in Sections 2 and 4 |
| Consumer roles covered | Covered in Section 3 |
| Read-only boundary documented | Covered in Section 4 |
| Source endpoint documented | Covered in Section 5 |
| Required permission documented | Covered in Sections 1 and 5 |
| Display rules documented | Covered in Section 6 |
| State display mapping documented | Covered in Section 7 |
| Error posture display documented | Covered in Section 8 |
| Allowed display fields documented | Covered in Section 9 |
| Support/audit-only fields documented | Covered in Section 10 |
| Never-displayed fields documented | Covered in Section 11 |
| Explicit non-goals documented | Covered in Section 12 |
| UX notes documented | Covered in Section 13 |
| Audit/logging expectations documented | Covered in Section 14 |
| Recommended next implementation slice documented | Covered in Section 15 |
