# ExitPass Central PMS to POS Server Fiscal Issuance Integration Contract v1.0 Review

## 1. Review Summary

This review covers the creation of `ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`.

The addendum defines how Central PMS should call POS Server for fiscal issuance, interpret current runtime responses, retry safely, record fiscal issuance references, and gate normal ExitAuthorization.

The addendum is aligned to the current POS Server runtime on `dev` and to the existing ExitPass v1.3 documentation baseline.

## 2. Repositories Inspected

| Repository | Purpose | Branch inspected |
| --- | --- | --- |
| `D:\SourceCodes\ExitPass` | Documentation repository modified by this task. | `docs/v1.3-central-pms-pos-server-fiscal-issuance-contract` |
| `D:\SourceCodes\ExitPass-PoSServer` | Runtime repository inspected read-only. | `dev` |

## 3. Runtime Files Inspected

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

## 4. Documentation References Inspected

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Alignment_Review.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

## 5. Files Created

- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0_Review.md`

## 6. Runtime Facts Confirmed

Confirmed from runtime source, tests, and runtime slice notes:

- `POST /v1/fiscal-documents/` exists.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` exists.
- POS Server currently derives idempotency key from `payableBasis.upstreamFinalityRef`.
- POS Server computes semantic request hash server-side.
- Same-key/same-hash requests replay the original fiscal document result.
- Same-key/different-hash requests fail closed as `fiscal_document_idempotency_conflict`.
- POS Server resolves fiscal identity server-side.
- POS Server resolves fiscal sequence policy server-side.
- POS Server locks the fiscal sequence-state row using row-level locking.
- POS Server allocates and persists fiscal numbering fields.
- First-time success returns `resultClassification = newly_created`.
- Replay returns `resultClassification = idempotent_replay`.
- Successful create/replay returns `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`.
- Successful create/replay returns `fiscalNumberAssignmentState = assigned`.
- Create and read responses expose `fiscalDocumentStatusCodeId`.
- Failure responses include `errorPosture` where useful.
- Incomplete fiscal numbering evidence fails closed as `fiscal_number_assignment_incomplete`.

## 7. Central PMS Preconditions Documented

The addendum documents that Central PMS may call POS Server only after:

- Site is resolved.
- Site POS Server context is determined.
- parking session/payment context is known.
- payment finality is verified by Central PMS.
- payable basis is approved and stable.
- statutory discount validation is complete where applicable.
- fiscal facts are ready.
- stable upstream finality reference exists.
- Central PMS is ready to record returned fiscal issuance evidence.

## 8. Request Construction Rules Documented

The addendum documents Central PMS responsibilities for:

- Site POS Server id/ref.
- fiscal document type id/key.
- fiscal document status code id.
- business day.
- Central PMS parking/payment references.
- approved payable basis.
- stable `payableBasis.upstreamFinalityRef`.
- approved discount references when applicable.
- document lines, tenders, tax details, totals, and reference context.
- exclusion of raw sensitive evidence, raw provider callbacks, card PAN/CVV, secrets, tokens, credentials, and uncontrolled raw payment payloads.

## 9. Idempotency and Retry Rules Documented

The addendum documents:

- current idempotency key source as `payableBasis.upstreamFinalityRef`.
- same upstream finality reference for the same fiscal issuance attempt.
- same semantic request body for retries after uncertain outcomes.
- no reuse of upstream finality reference for semantically different issuance.
- same key + same hash = safe replay.
- same key + different hash = conflict/fail closed.
- replay does not allocate another fiscal number.
- conflict does not allocate a fiscal number.

## 10. Response Handling Documented

The addendum documents:

- `202 accepted` with `resultClassification = newly_created`.
- `202 accepted` with `resultClassification = idempotent_replay`.
- replay reconciliation without duplicate Central PMS fiscal reference creation.
- `409 fiscal_document_idempotency_conflict` fail-closed behavior.
- `400` request correction and fiscal setup/configuration correction handling.
- `503` persistence/service/fiscal numbering evidence failure handling.
- `errorPosture` interpretation.
- `fiscal_number_assignment_incomplete` fail-closed behavior.

## 11. Fiscal Reference Recording Rules Documented

The addendum documents that Central PMS should record at least:

- POS Server fiscal document id.
- fiscal identity id.
- fiscal sequence policy id.
- fiscal sequence value.
- fiscal document number.
- fiscal series.
- prefix/suffix at assignment time.
- fiscal number assigned at.
- fiscal number assigned by ref.
- fiscal document status code id.
- result classification.
- fiscal issuance evidence status.
- fiscal number assignment state.
- upstream finality reference.
- Central PMS payment/session references.
- request hash or correlation reference if available.
- POS Server response timestamp.
- retry/replay/conflict history.

Any Central PMS schema/API gap remains a downstream implementation item.

## 12. ExitAuthorization Gating Rules Documented

The addendum documents that normal ExitAuthorization must not be issued until:

1. Central PMS has verified payment finality.
2. Central PMS has successfully called POS Server or replayed successful fiscal issuance.
3. POS Server returned `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`.
4. POS Server returned `fiscalNumberAssignmentState = assigned`.
5. Central PMS durably recorded the fiscal issuance reference.

Fiscal issuance failure, unknown outcome, or incomplete evidence blocks normal ExitAuthorization unless separately approved exception/manual-release policy applies.

## 13. GET Readback and Reconciliation Documented

The addendum documents GET readback use for:

- uncertain POST outcome.
- `503` with known fiscal document id.
- reconciliation.
- verifying existing fiscal reference.
- duplicate/replay ambiguity.
- operator review.

It also documents that GET readback is not payment finality, ExitAuthorization, gate command, BIR report finality, Digital SI finality, entitlement approval, manual release approval, or continuity activation.

## 14. Authority Boundaries Preserved

The addendum preserves:

- Central PMS owns payment finality.
- Central PMS owns payment-linked platform control state.
- Central PMS records fiscal issuance reference.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Channels and terminals are not independent POS systems.

## 15. Deferred Items Preserved

The addendum does not create endpoint contracts for:

- Digital SI.
- printable SI rendering.
- QR presentation.
- X-read.
- Z-read.
- BIR Sales Summary.
- Annex E.
- Electronic Journal.
- POSLog.
- reprints.
- void/refund/cancel/return fiscal adjustments.
- reset counter mechanics.
- Z-counter mechanics.
- Grand Total Amount mechanics.
- recovery automation.
- gate opening.
- ExitAuthorization endpoint.
- continuity activation.
- manual release approval.

## 16. Issues or Mismatches

No runtime contradiction was found for the addendum scope.

No source, SQL, migrations, generated artifacts, DOCX files, or runtime repository files were modified.

## 17. Recommended Next Task

Recommended next task:

> Draft the Central PMS implementation planning note for fiscal issuance reference persistence and exception-state handling, including schema/API gaps, event/audit fields, and operator/reconciliation queues needed to consume this integration contract safely.
